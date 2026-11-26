using CSVC.Core.Accounts;
using CSVC.UseCases.Common;
using CSVC.UseCases.Devices.Models;
using CSVC.UseCases.Exceptions;
using CSVC.UseCases.Maintenance;
using CSVC.UseCases.ServiceOrders.DTOs;

namespace CSVC.UseCases.Devices;

public sealed partial class DeviceService
{
    public async Task<MaintenanceScheduleDto> GetMaintenanceScheduleAsync(string deviceCode, CancellationToken ct)
    {
        var actor = await RequireActorAsync(false, ct);
        var device = await GetMaintenanceDeviceAsync(deviceCode, actor, ct);
        return await maintenance.GetAsync(device.Id, device.CategoryId, ct);
    }

    public Task<MaintenanceScheduleDto> ConfigureMaintenanceScheduleAsync(string deviceCode, CreateMaintenanceScheduleRequest request, CancellationToken ct)
        => transaction.ExecuteAsync(async token =>
        {
            var actor = await RequireActorAsync(true, token);
            Validate(request);
            var device = await GetMaintenanceDeviceAsync(deviceCode, actor, token);
            await transaction.LockDevicesAsync([device.Id], token);
            var entity = await unitOfWork.Devices.GetByIdAsync(device.Id, token) ?? throw Missing();
            return await maintenance.ConfigureLockedAsync(entity, request.StartOn!.Value, actor.AccountId, token);
        }, ct);

    public async Task<PagedResult<MaintenancePeriodDto>> ListMaintenancePeriodsAsync(string? deviceCode,
        MaintenancePeriodQuery query, CancellationToken ct)
    {
        var actor = await RequireActorAsync(false, ct);
        Validate(query);
        if (query.DueFrom > query.DueTo) throw Validation("dueFrom cannot be later than dueTo.");
        var device = deviceCode is null ? null : await GetMaintenanceDeviceAsync(deviceCode, actor, ct);
        var departmentId = actor.RoleCode == RoleCodes.DepartmentStaff ? (Guid?)actor.DepartmentId : null;
        return await maintenanceRepository.ListAsync(query, device?.Id, departmentId, await CanViewMaintenanceOrdersAsync(actor, ct), maintenance.Today, ct);
    }

    public async Task<PagedResult<MaintenanceAttemptDto>> ListUnscheduledMaintenanceAsync(string deviceCode,
        ServiceHistoryQuery query, CancellationToken ct)
    {
        var actor = await RequireActorAsync(true, ct);
        await equipment.RequireMemberAsync(actor.RoleCode, actor.DepartmentId, ct);
        Validate(query);
        var device = await GetMaintenanceDeviceAsync(deviceCode, actor, ct);
        return await maintenanceRepository.ListUnscheduledAsync(device.Id, query, ct);
    }

    private async Task<DeviceReadModel> GetMaintenanceDeviceAsync(string deviceCode, Actor actor, CancellationToken ct)
        => await unitOfWork.Devices.GetByCodeAsync(ValidateDeviceCode(Uri.UnescapeDataString(deviceCode)),
            actor.RoleCode == RoleCodes.DepartmentStaff ? actor.DepartmentId : null, ct) ?? throw Missing();

    private async Task<bool> CanViewMaintenanceOrdersAsync(Actor actor, CancellationToken ct)
    {
        if (actor.RoleCode == RoleCodes.DepartmentStaff) return false;
        try
        {
            await equipment.RequireMemberAsync(actor.RoleCode, actor.DepartmentId, ct);
            return true;
        }
        catch (UseCaseException exception) when (exception.Code is "EQUIPMENT_DEPARTMENT_NOT_CONFIGURED"
            or "EQUIPMENT_DEPARTMENT_UNAVAILABLE" or "EQUIPMENT_ACCOUNT_DEPARTMENT_MISMATCH")
        {
            return false;
        }
    }
}
