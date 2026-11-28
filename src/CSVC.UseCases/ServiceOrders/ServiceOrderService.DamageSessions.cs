using System.Text.Json;
using CSVC.Core.Common;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;

namespace CSVC.UseCases.ServiceOrders;

public sealed partial class ServiceOrderService
{
    private async Task ApplyDamageRepairResultLockedAsync(ServiceOrder order, ServiceOrderItem item, DateTimeOffset? receivedAt,
        CancellationToken ct)
    {
        if (item.DamageSessionId is not { } sessionId || item.ExecutionStatus != ServiceItemStatus.Completed ||
            item.ExecutionMethod == ExecutionMethod.External && receivedAt is null ||
            item.ResultRecordedAt is not { } recordedAt || item.ResultRecordedByAccountId is not { } recordedBy)
            return;

        var session = await damageSessions.FindOpenAsync(item.DeviceId, ct);
        if (session?.Id != sessionId) return;

        var device = await unitOfWork.Devices.GetByIdAsync(item.DeviceId, ct) ?? throw Missing("DEVICE_NOT_FOUND");
        if (device.Condition != DeviceCondition.Damaged) return;

        var closedAt = recordedAt > session.OpenedAt ? recordedAt : session.OpenedAt;
        if (receivedAt is { } received && received > closedAt) closedAt = received;
        session.ClosedAt = closedAt;
        session.ClosedByAccountId = recordedBy;
        session.Version++;
        device.Condition = DeviceCondition.Good;
        device.Version++;

        await unitOfWork.DeviceHistories.AddAsync(new DeviceHistory
        {
            DeviceId = device.Id,
            // A result request may include several items, each with its own device history.
            RequestId = item.Id,
            ActorAccountId = recordedBy,
            ActionCode = "DEVICE_DAMAGE_RESOLVED",
            Outcome = AuditOutcome.Success,
            OccurredAt = closedAt,
            ChangedFields = JsonSerializer.Serialize(new[] { "condition" }),
            BeforeData = JsonSerializer.Serialize(new { condition = DeviceCondition.Damaged.ToString() }),
            AfterData = JsonSerializer.Serialize(new
            {
                condition = DeviceCondition.Good.ToString(), sessionId, sourceCode = order.DisplayCode,
                serviceOrderItemId = item.Id, resultRequestId = item.ResultRequestId
            })
        }, ct);
    }
}
