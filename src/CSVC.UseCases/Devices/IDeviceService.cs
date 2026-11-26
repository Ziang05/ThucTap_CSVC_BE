using CSVC.UseCases.Common;
using CSVC.UseCases.Devices.DTOs;
using CSVC.UseCases.Maintenance;
using CSVC.UseCases.ServiceOrders.DTOs;

namespace CSVC.UseCases.Devices;

public interface IDeviceService
{
    Task<MaintenanceScheduleDto> GetMaintenanceScheduleAsync(string deviceCode, CancellationToken ct);
    Task<MaintenanceScheduleDto> ConfigureMaintenanceScheduleAsync(string deviceCode, CreateMaintenanceScheduleRequest request, CancellationToken ct);
    Task<PagedResult<MaintenancePeriodDto>> ListMaintenancePeriodsAsync(string? deviceCode, MaintenancePeriodQuery query, CancellationToken ct);
    Task<PagedResult<MaintenanceAttemptDto>> ListUnscheduledMaintenanceAsync(string deviceCode, ServiceHistoryQuery query, CancellationToken ct);
    Task<PagedResult<BorrowedDeviceDto>> BorrowedAsync(BorrowedDeviceListQuery query, CancellationToken cancellationToken);
    Task<PagedResult<DeviceDto>> ListAsync(DeviceListQuery query, CancellationToken cancellationToken);
    Task<DeviceDto> GetAsync(string deviceCode, CancellationToken cancellationToken);
    Task<DeviceDto> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken);
    Task<DeviceDto> CreateAsync(CreateDeviceRequest request, CancellationToken cancellationToken);
    Task UpdateAsync(string deviceCode, UpdateDeviceRequest request, CancellationToken cancellationToken);
    Task PutIntoUseBatchAsync(PutDeviceIntoUseBatchRequest request, CancellationToken cancellationToken);
    Task PutIntoUseAsync(string deviceCode, PutDeviceIntoUseRequest request, CancellationToken cancellationToken);
    Task SetParentAsync(string deviceCode, string? parentDeviceCode, CancellationToken cancellationToken);
    Task<PagedResult<DeviceHistoryDto>> HistoryAsync(string deviceCode, DeviceHistoryQuery query, CancellationToken cancellationToken);
    Task<PagedResult<DeviceHistoryDto>> GlobalHistoryAsync(DeviceHistoryQuery query, CancellationToken cancellationToken);
}
