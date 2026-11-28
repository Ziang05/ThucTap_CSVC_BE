using CSVC.Core.Devices;

namespace CSVC.UseCases.DamageReports;

internal static class DamageSessionCondition
{
    // A custody handover alone does not resolve an open damage session.
    public static async Task<DeviceCondition> PreserveOpenDamageAsync(this IDamageSessionRepository sessions,
        Guid deviceId, DeviceCondition reported, CancellationToken ct)
        => reported == DeviceCondition.Good && await sessions.FindOpenAsync(deviceId, ct) is not null
            ? DeviceCondition.Damaged : reported;
}
