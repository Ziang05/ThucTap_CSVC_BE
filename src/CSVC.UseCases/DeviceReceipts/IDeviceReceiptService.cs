using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceReceipts.DTOs;

namespace CSVC.UseCases.DeviceReceipts;

public interface IDeviceReceiptService
{
    Task<PagedResult<DeviceReceiptDto>> ListAsync(DeviceReceiptListQuery query, CancellationToken cancellationToken);
    Task<DeviceReceiptDto> GetAsync(string receiptCode, CancellationToken cancellationToken);
    Task<DeviceReceiptDto> CreateAsync(CreateDeviceReceiptRequest request, CancellationToken cancellationToken);
    Task UpdateAsync(string receiptCode, UpdateDeviceReceiptRequest request, CancellationToken cancellationToken);
    Task DecideAsync(string receiptCode, bool approve, ReceiptDecisionRequest request, CancellationToken cancellationToken);
    Task CompleteAsync(string receiptCode, CancellationToken cancellationToken);
    Task CancelAsync(string receiptCode, CancellationToken cancellationToken);
    Task<PagedResult<DeviceReceiptHistoryDto>> HistoryAsync(string receiptCode, DeviceReceiptHistoryQuery query, CancellationToken cancellationToken);
}
