using CSVC.UseCases.Common;
using CSVC.UseCases.Devices.DTOs;

namespace CSVC.UseCases.Devices;

public interface IDeviceService
{
    Task<PagedResult<DeviceDto>> ListAsync(DeviceListQuery query, CancellationToken cancellationToken);
    Task<DeviceDto> GetAsync(string deviceCode, CancellationToken cancellationToken);
    Task<DeviceDto> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken);
    Task<DeviceDto> CreateAsync(CreateDeviceRequest request, CancellationToken cancellationToken);
    Task UpdateAsync(string deviceCode, UpdateDeviceRequest request, CancellationToken cancellationToken);
    Task SetParentAsync(string deviceCode, string? parentDeviceCode, CancellationToken cancellationToken);
    Task SetLockedAsync(string deviceCode, bool locked, CancellationToken cancellationToken);
    Task<PagedResult<DeviceHistoryDto>> HistoryAsync(string deviceCode, DeviceHistoryQuery query, CancellationToken cancellationToken);
    Task<PagedResult<DeviceHistoryDto>> GlobalHistoryAsync(DeviceHistoryQuery query, CancellationToken cancellationToken);
}
