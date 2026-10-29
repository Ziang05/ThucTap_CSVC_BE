using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceTransfers.DTOs;

namespace CSVC.UseCases.DeviceTransfers;

public interface IDeviceTransferService
{
    Task<PagedResult<DeviceTransferDto>> ListAsync(DeviceTransferListQuery query, CancellationToken cancellationToken);
    Task<DeviceTransferDto> GetAsync(string transferCode, CancellationToken cancellationToken);
    Task<DeviceTransferDto> CreateAsync(CreateDeviceTransferRequest request, CancellationToken cancellationToken);
    Task UpdateAsync(string transferCode, UpdateDeviceTransferRequest request, CancellationToken cancellationToken);
    Task DecideAsync(string transferCode, bool approve, TransferDecisionRequest request, CancellationToken cancellationToken);
    Task HandoverCompleteAsync(string transferCode, TransferHandoverRequest request, CancellationToken cancellationToken);
    Task CancelAsync(string transferCode, CancellationToken cancellationToken);
    Task<PagedResult<DeviceTransferHistoryDto>> HistoryAsync(string transferCode, DeviceTransferHistoryQuery query, CancellationToken cancellationToken);
}
