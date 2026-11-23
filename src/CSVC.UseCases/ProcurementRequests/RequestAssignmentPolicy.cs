using CSVC.Core.Accounts;
using CSVC.Core.ProcurementRequests;
using CSVC.Core.Materials;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Exceptions;
using CSVC.UseCases.Settings;

namespace CSVC.UseCases.ProcurementRequests;

// The source request is the responsibility boundary; no second task aggregate is created.
public interface IRequestAssignmentPolicy
{
    Task RequireAsync(string? sourceCode, Guid actorId, CancellationToken ct);
    Task RequireIdAsync(Guid? sourceId, Guid actorId, CancellationToken ct, bool allowClosed = false);
    Task<bool> RequireDocumentAsync(RequestDocumentKind kind, string documentCode, Guid actorId, CancellationToken ct, bool allowClosed = false);
    Task RequireMaterialAsync(MaterialDocument document, Guid actorId, CancellationToken ct);
}
public sealed class RequestAssignmentPolicy(IProcurementRequestRepository requests, IRepository<ProcurementRequest> records,
    IRepository<ProcurementDocumentLink> links, IServiceOrderRepository services, IUnitOfWork uow,
    IEquipmentDepartmentPolicy equipment) : IRequestAssignmentPolicy
{
    public async Task RequireAsync(string? code, Guid actorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        var request = await requests.GetAsync(code, false, ct) ?? throw new UseCaseException(FailureKind.NotFound, "SOURCE_REQUEST_NOT_FOUND", "Source request not found.");
        await RequireOwnerAsync(request, actorId, ct, false);
    }
    public async Task RequireIdAsync(Guid? id, Guid actorId, CancellationToken ct, bool allowClosed = false)
    {
        if (id is null) return;
        await RequireOwnerAsync(await records.GetByIdAsync(id.Value, ct) ?? throw new UseCaseException(FailureKind.NotFound, "SOURCE_REQUEST_NOT_FOUND", "Source request not found."), actorId, ct, allowClosed);
    }
    private async Task RequireOwnerAsync(ProcurementRequest request, Guid actorId, CancellationToken ct, bool allowClosed)
    {
        if (request.AssigneeAccountId is null) throw new UseCaseException(FailureKind.Conflict, "SOURCE_REQUEST_UNASSIGNED", "The QTTB manager must assign this request before execution.");
        var account = await uow.Accounts.GetByIdAsync(actorId, ct);
        var role = account is null ? null : await uow.Roles.GetByIdAsync(account.RoleId, ct);
        if (request.AssigneeAccountId != actorId || account is null || account.IsLock || role?.Code != RoleCodes.EquipmentStaff)
            throw new UseCaseException(FailureKind.Forbidden, "SOURCE_REQUEST_ASSIGNEE_REQUIRED", "Only the employee currently assigned to this source request may execute it.");
        var profile = await uow.UserProfiles.GetByIdAsync(account.UserProfileId, ct);
        if (profile is null) throw new UseCaseException(FailureKind.Forbidden, "SOURCE_REQUEST_ASSIGNEE_REQUIRED", "The assignee profile is unavailable.");
        await equipment.RequireMemberAsync(role.Code, profile.DepartmentId, ct);
        if (!allowClosed && request.Status is (ProcurementRequestStatus.Completed or ProcurementRequestStatus.Cancelled))
            throw new UseCaseException(FailureKind.Conflict, "SOURCE_REQUEST_CLOSED", "The source request is closed.");
    }
    public async Task<bool> RequireDocumentAsync(RequestDocumentKind kind, string code, Guid actorId, CancellationToken ct, bool allowClosed = false)
    {
        var key = CSVC.Core.Common.BusinessCode.Normalize(code);
        var link = await links.FirstOrDefaultAsync(l => l.Kind == kind && l.DocumentCode == key, ct);
        if (link is not null) await RequireIdAsync(link.RequestId, actorId, ct, allowClosed);
        return link is not null;
    }
    public async Task RequireMaterialAsync(MaterialDocument document, Guid actorId, CancellationToken ct)
    {
        await RequireIdAsync(document.ProcurementRequestId, actorId, ct);
        if (document.ServiceOrderId is { } serviceId)
        {
            var service = await services.GetByIdAsync(serviceId, ct);
            if (service is not null) await RequireDocumentAsync(RequestDocumentKind.Repair, service.DisplayCode, actorId, ct);
        }
    }
}
