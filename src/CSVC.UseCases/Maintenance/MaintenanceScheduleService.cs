using System.Text.Json;
using CSVC.Core.Common;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.DeviceCategories;
using CSVC.UseCases.Exceptions;

namespace CSVC.UseCases.Maintenance;

public sealed class MaintenanceScheduleService(IUnitOfWork unitOfWork, IInventoryTransaction transaction,
    IRepository<DeviceMaintenanceSchedule> schedules, IRepository<DeviceMaintenancePeriod> periods,
    IMaintenanceScheduleRepository repository, TimeProvider clock)
{
    private static readonly TimeZoneInfo LocalTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    internal DateOnly Today => VietnamDate(clock.GetUtcNow());

    private static DateOnly VietnamDate(DateTimeOffset time)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, LocalTimeZone).DateTime);

    public async Task<MaintenanceScheduleDto> GetAsync(Guid deviceId, Guid categoryId, CancellationToken ct)
    {
        var schedule = await schedules.FirstOrDefaultAsync(x => x.DeviceId == deviceId, ct);
        var source = await SourceAsync(categoryId, ct);
        return ToDto(schedule, source, schedule is null ? null : await repository.FirstDueOnAsync(deviceId, ct));
    }

    public async Task<MaintenanceScheduleDto> ConfigureLockedAsync(Device device, DateOnly startOn, Guid actorId, CancellationToken ct)
    {
        var schedule = await schedules.GetByIdAsync(device.Id, ct);
        var source = await SourceAsync(device.CategoryId, ct);
        if (schedule is not null)
        {
            if (schedule.StartOn != startOn)
                throw Conflict("MAINTENANCE_SCHEDULE_ALREADY_CONFIGURED", "The initial maintenance date is already set.");
            return ToDto(schedule, source, await repository.FirstDueOnAsync(device.Id, ct));
        }
        if (source?.MaintenanceIntervalMonths is not { } months)
            throw Validation("The device category has no effective maintenance interval.");
        if (startOn == default || !CanOpen(AddMonths(startOn, months, startOn.Day)))
            throw Validation("The first maintenance due date is outside the supported date range.");
        var now = clock.GetUtcNow();
        schedule = new DeviceMaintenanceSchedule
        {
            DeviceId = device.Id, StartOn = startOn, CreatedByAccountId = actorId, CreatedAt = now, UpdatedAt = now
        };
        await schedules.AddAsync(schedule, ct);
        await AuditAsync(device.Id, actorId, "MAINTENANCE_SCHEDULE_CONFIGURED", null, new { startOn, months, source.DisplayCode }, ct);
        var initial = await AddPeriodAsync(device.Id, InitialDueOn(startOn, months, Today), source, ct);
        await AdvanceAsync(schedule, initial, source, Today, ct);
        return ToDto(schedule, source, initial.DueOn);
    }

    public async Task AdvanceAllAsync(CancellationToken ct)
    {
        var today = Today;
        var failures = new List<Exception>();
        foreach (var deviceId in await repository.DevicesToAdvanceAsync(today, ct))
        {
            try
            {
                await transaction.ExecuteAsync(async token =>
                {
                    await transaction.LockDevicesAsync([deviceId], token);
                    var device = await unitOfWork.Devices.FirstOrDefaultAsync(x => x.Id == deviceId, token);
                    if (device is not null) await AdvanceLockedAsync(device, today, token);
                    return true;
                }, ct);
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                failures.Add(new InvalidOperationException($"Unable to advance maintenance for device {deviceId}.", exception));
            }
        }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    public async Task AdvanceLockedAsync(Device device, CancellationToken ct)
        => await AdvanceLockedAsync(device, Today, ct);

    private async Task AdvanceLockedAsync(Device device, DateOnly today, CancellationToken ct)
    {
        var schedule = await schedules.GetByIdAsync(device.Id, ct);
        if (schedule is null) return;
        var latest = await repository.LatestTrackedAsync(device.Id, ct);
        if (latest is null || latest.Status == MaintenancePeriodStatus.Scheduled && latest.DueOn > today) return;
        await AdvanceAsync(schedule, latest, await SourceAsync(device.CategoryId, ct), today, ct);
    }

    private async Task AdvanceAsync(DeviceMaintenanceSchedule schedule, DeviceMaintenancePeriod latest,
        DeviceCategory? source, DateOnly today, CancellationToken ct)
    {
        // An already opened last period had no published successor. Its stored close marks an interruption
        // even if the worker first sees the restored category after that date.
        if (latest.Status != MaintenancePeriodStatus.Scheduled && latest.ClosesOn is { } closedOn && closedOn <= today)
            await PauseAsync(schedule, closedOn, ct);

        while (true)
        {
            if (latest.Status == MaintenancePeriodStatus.Scheduled)
            {
                if (latest.DueOn > today) return;
                var previous = await periods.FirstOrDefaultTrackedAsync(p => p.DeviceId == schedule.DeviceId
                    && p.Status == MaintenancePeriodStatus.Due, ct);
                if (previous is not null)
                {
                    await MarkMissedAsync(previous, ct);
                    // Release the filtered Due index entry before opening the next period in the same transaction.
                    await unitOfWork.CommitAsync(ct);
                }
                latest.Status = MaintenancePeriodStatus.Due;
                latest.ClosesOn = NextOn(latest.DueOn, source, schedule.StartOn.Day)
                    ?? AddMonths(latest.DueOn, latest.IntervalMonths, schedule.StartOn.Day) ?? DateOnly.MaxValue;
                latest.UpdatedAt = clock.GetUtcNow();
                await AuditAsync(schedule.DeviceId, null, "MAINTENANCE_PERIOD_OPENED", new { status = "Scheduled" },
                    new { latest.Id, latest.DueOn, latest.ClosesOn, status = "Due" }, ct);
                // Release the filtered Scheduled index entry before inserting a successor.
                await unitOfWork.CommitAsync(ct);
            }

            var closesOn = latest.ClosesOn!.Value;
            if (latest.Status == MaintenancePeriodStatus.Due && closesOn <= today)
            {
                await MarkMissedAsync(latest, ct);
                await unitOfWork.CommitAsync(ct);
            }

            var successor = closesOn;
            if (schedule.PausedOn.HasValue && source?.MaintenanceIntervalMonths is { } resumedMonths
                && AddMonths(schedule.StartOn, resumedMonths, schedule.StartOn.Day) is not null)
            {
                var resumedOn = InitialDueOn(schedule.StartOn, resumedMonths, today);
                if (resumedOn > successor) successor = resumedOn;
            }
            if (NextOn(successor, source, schedule.StartOn.Day) is null)
            {
                if (closesOn <= today) await PauseAsync(schedule, closesOn, ct);
                return;
            }

            if (schedule.PausedOn is { } pausedOn)
            {
                schedule.PausedOn = null;
                Touch(schedule);
                await AuditAsync(schedule.DeviceId, null, "MAINTENANCE_SCHEDULE_RESUMED", new { pausedOn }, new { resumedOn = today }, ct);
            }
            latest = await AddPeriodAsync(schedule.DeviceId, successor, source!, ct);
        }
    }

    public async Task<DeviceMaintenancePeriod?> ResolveOrderPeriodLockedAsync(Device device, Guid? requestedPeriodId, CancellationToken ct)
    {
        var today = Today;
        await AdvanceLockedAsync(device, today, ct);
        if (requestedPeriodId is { } id)
            return await RequireDueLockedAsync(id, device, true, ct);
        var period = await repository.FindOpenPeriodAsync(device.Id, today, ct);
        if (period is not null) await RequireNoActiveOrderAsync(period.Id, device, ct);
        return period;
    }

    public async Task<DeviceMaintenancePeriod> RequireDueLockedAsync(Guid periodId, Device device, bool checkActiveAttempt, CancellationToken ct)
    {
        var period = await periods.GetByIdAsync(periodId, ct);
        if (period is null || period.DeviceId != device.Id)
            throw Validation("The maintenance period must belong to the selected device.");
        var today = Today;
        if (period.Status is not (MaintenancePeriodStatus.Due or MaintenancePeriodStatus.Missed)
            || period.DueOn > today || period.ClosesOn is not { } closesOn || closesOn <= today)
            throw Conflict("MAINTENANCE_PERIOD_NOT_DUE", $"The maintenance period for {device.DeviceCode} is no longer open.");
        if (checkActiveAttempt) await RequireNoActiveOrderAsync(periodId, device, ct);
        return period;
    }

    private async Task RequireNoActiveOrderAsync(Guid periodId, Device device, CancellationToken ct)
    {
        if (await repository.HasActiveAttemptAsync(periodId, ct))
            throw Conflict("MAINTENANCE_PERIOD_ACTIVE_ORDER", $"An active maintenance order already covers {device.DeviceCode} for this period.");
    }

    public async Task<MaintenancePeriodStatus?> RecordResultLockedAsync(ServiceOrderItem item, ServiceItemStatus result,
        DateOnly? requestedDate, DateTimeOffset recordedAt, CancellationToken ct)
    {
        if (item.MaintenancePeriodId is not { } periodId || result != ServiceItemStatus.Completed)
        {
            if (requestedDate is not null) throw Validation("The performed date applies only to successful maintenance linked to a period.");
            return null;
        }
        var period = await periods.GetByIdAsync(periodId, ct)
            ?? throw Conflict("MAINTENANCE_PERIOD_MISSING", "The maintenance period could not be found.");
        // Older clients may still send a date. The recorded result timestamp is authoritative.
        var performedOn = VietnamDate(recordedAt);
        if (performedOn < period.DueOn || period.ClosesOn is not { } closesOn)
            throw Conflict("MAINTENANCE_PERIOD_NOT_DUE", "The maintenance period has not opened.");
        item.PerformedOn = performedOn;
        if (performedOn >= closesOn) return null;

        if (period.Status is not (MaintenancePeriodStatus.Due or MaintenancePeriodStatus.Missed))
            throw Conflict("MAINTENANCE_PERIOD_ALREADY_RESOLVED", "The maintenance period cannot be completed again.");
        var before = period.Status;
        period.Status = MaintenancePeriodStatus.Completed;
        period.CompletedByItemId = item.Id;
        period.UpdatedAt = recordedAt;
        return before;
    }

    private async Task<DeviceMaintenancePeriod> AddPeriodAsync(Guid deviceId, DateOnly dueOn, DeviceCategory source, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var period = new DeviceMaintenancePeriod
        {
            DeviceId = deviceId, DueOn = dueOn, IntervalMonths = source.MaintenanceIntervalMonths!.Value,
            IntervalSourceCategoryId = source.Id, CreatedAt = now, UpdatedAt = now
        };
        await periods.AddAsync(period, ct);
        await AuditAsync(deviceId, null, "MAINTENANCE_PERIOD_SCHEDULED", null, new { period.Id, dueOn, period.IntervalMonths }, ct);
        return period;
    }

    private async Task MarkMissedAsync(DeviceMaintenancePeriod period, CancellationToken ct)
    {
        period.Status = MaintenancePeriodStatus.Missed;
        period.UpdatedAt = clock.GetUtcNow();
        await AuditAsync(period.DeviceId, null, "MAINTENANCE_PERIOD_MISSED", new { status = "Due" },
            new { period.Id, period.DueOn, period.ClosesOn, status = "Missed" }, ct);
    }

    private async Task<DeviceCategory?> SourceAsync(Guid categoryId, CancellationToken ct)
    {
        var categories = (await unitOfWork.DeviceCategories.GetAllAsync(ct)).ToDictionary(category => category.Id);
        return categories.TryGetValue(categoryId, out var category)
            ? CategoryHierarchy.WalkToRoot(category, categories).FirstOrDefault(node => node.MaintenanceIntervalMonths > 0)
            : null;
    }

    private Task AuditAsync(Guid deviceId, Guid? actorId, string action, object? before, object after, CancellationToken ct)
        => unitOfWork.DeviceHistories.AddAsync(new DeviceHistory
        {
            RequestId = Guid.NewGuid(), ActorAccountId = actorId, DeviceId = deviceId, ActionCode = action,
            Outcome = AuditOutcome.Success, OccurredAt = clock.GetUtcNow(),
            BeforeData = before is null ? null : JsonSerializer.Serialize(before), AfterData = JsonSerializer.Serialize(after)
        }, ct);

    private async Task PauseAsync(DeviceMaintenanceSchedule schedule, DateOnly closedOn, CancellationToken ct)
    {
        if (schedule.PausedOn is not null) return;
        schedule.PausedOn = closedOn;
        Touch(schedule);
        await AuditAsync(schedule.DeviceId, null, "MAINTENANCE_SCHEDULE_PAUSED", null, new { schedule.PausedOn }, ct);
    }

    private void Touch(DeviceMaintenanceSchedule schedule)
    {
        schedule.UpdatedAt = clock.GetUtcNow();
        schedule.Version++;
    }

    private static MaintenanceScheduleDto ToDto(DeviceMaintenanceSchedule? schedule, DeviceCategory? source, DateOnly? firstDueOn)
        => new(schedule?.StartOn, schedule?.PausedOn, source?.MaintenanceIntervalMonths, source?.DisplayCode, firstDueOn);

    private static DateOnly? NextOn(DateOnly dueOn, DeviceCategory? source, int anchorDay)
    {
        var next = source?.MaintenanceIntervalMonths is { } months ? AddMonths(dueOn, months, anchorDay) : null;
        return CanOpen(next) ? next : null;
    }

    private static bool CanOpen(DateOnly? date) => date is { } dueOn && dueOn < DateOnly.MaxValue;

    private static DateOnly InitialDueOn(DateOnly startOn, int months, DateOnly today)
    {
        var elapsedMonths = (today.Year - startOn.Year) * 12 + today.Month - startOn.Month;
        var completedIntervals = elapsedMonths / months;
        var candidate = AddMonths(startOn, Math.Max(1, completedIntervals) * months, startOn.Day)!.Value;
        return candidate > today && completedIntervals > 1
            ? AddMonths(startOn, (completedIntervals - 1) * months, startOn.Day)!.Value
            : candidate;
    }

    private static DateOnly? AddMonths(DateOnly from, int months, int anchorDay)
    {
        var month = (long)(from.Year - 1) * 12 + from.Month - 1 + months;
        if (month is < 0 or >= 9999 * 12) return null;
        var year = (int)(month / 12) + 1;
        var monthOfYear = (int)(month % 12) + 1;
        return new DateOnly(year, monthOfYear, Math.Min(anchorDay, DateTime.DaysInMonth(year, monthOfYear)));
    }

    private static UseCaseException Validation(string message) => new(FailureKind.Validation, "MAINTENANCE_SCHEDULE_INVALID", message);
    private static UseCaseException Conflict(string code, string message) => new(FailureKind.Conflict, code, message);
}
