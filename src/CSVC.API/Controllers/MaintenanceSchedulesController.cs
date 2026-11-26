using CSVC.UseCases.Devices;
using CSVC.UseCases.Maintenance;
using CSVC.UseCases.ServiceOrders.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MaintenanceSchedulesController(IDeviceService devices) : AuthenticatedApiControllerBase
{
    [HttpGet("devices/{deviceCode}/maintenance-schedule")]
    public async Task<IActionResult> GetSchedule(string deviceCode, CancellationToken ct)
        => Ok(await devices.GetMaintenanceScheduleAsync(deviceCode, ct));

    [HttpPut("devices/{deviceCode}/maintenance-schedule")]
    public async Task<IActionResult> ConfigureSchedule(string deviceCode, CreateMaintenanceScheduleRequest request, CancellationToken ct)
        => Ok(await devices.ConfigureMaintenanceScheduleAsync(deviceCode, request, ct));

    [HttpGet("devices/{deviceCode}/maintenance-periods")]
    public async Task<IActionResult> DevicePeriods(string deviceCode, [FromQuery] MaintenancePeriodQuery query, CancellationToken ct)
        => Ok(await devices.ListMaintenancePeriodsAsync(deviceCode, query, ct));

    [HttpGet("maintenance-periods")]
    public async Task<IActionResult> Periods([FromQuery] MaintenancePeriodQuery query, CancellationToken ct)
        => Ok(await devices.ListMaintenancePeriodsAsync(null, query, ct));

    [HttpGet("devices/{deviceCode}/maintenance-orders-outside-schedule")]
    public async Task<IActionResult> OrdersOutsideSchedule(string deviceCode, [FromQuery] ServiceHistoryQuery query, CancellationToken ct)
        => Ok(await devices.ListUnscheduledMaintenanceAsync(deviceCode, query, ct));
}
