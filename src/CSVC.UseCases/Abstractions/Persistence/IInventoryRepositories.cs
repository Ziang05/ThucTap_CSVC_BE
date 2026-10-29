using CSVC.Core.DeviceLoans;
using CSVC.Core.DeviceReceipts;
using CSVC.Core.DeviceTransfers;
using CSVC.Core.Devices;
using CSVC.UseCases.Common;
using CSVC.UseCases.Devices.DTOs;
using CSVC.UseCases.Devices.Models;
using CSVC.UseCases.DeviceReceipts.DTOs;
using CSVC.UseCases.DeviceReceipts.Models;
using CSVC.UseCases.DeviceLoans.DTOs;
using CSVC.UseCases.DeviceLoans.Models;
using CSVC.UseCases.DeviceTransfers.DTOs;
using CSVC.UseCases.DeviceTransfers.Models;

namespace CSVC.UseCases.Abstractions.Persistence;

public interface IDeviceRepository : IRepository<Device>
{
    Task<PagedResult<DeviceReadModel>> ListAsync(DeviceListQuery query, Guid? departmentScope, CancellationToken cancellationToken = default);
    Task<DeviceReadModel?> GetByCodeAsync(string deviceCode, Guid? departmentScope, CancellationToken cancellationToken = default);
    Task<DeviceReadModel?> GetByBarcodeAsync(string barcode, Guid? departmentScope, CancellationToken cancellationToken = default);
    Task<int> CountByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task<int> CountByManufacturerAsync(Guid manufacturerId, CancellationToken cancellationToken = default);
    Task<int> CountByAreaAsync(Guid areaId, bool includeDescendants, CancellationToken cancellationToken = default);
}
public interface IDeviceIdentityRepository : IRepository<DeviceIdentity> { }
public interface IDeviceHistoryRepository : IRepository<DeviceHistory>
{
    Task<PagedResult<DeviceHistoryReadModel>> ListForDeviceAsync(Guid deviceId, DeviceHistoryQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<DeviceHistoryReadModel>> ListTimelineForDeviceAsync(Guid deviceId, DeviceHistoryQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<DeviceHistoryReadModel>> ListGlobalTimelineAsync(DeviceHistoryQuery query, CancellationToken cancellationToken = default);
}
public interface IDeviceOperationHoldRepository
{
    Task<bool> ExistsForDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default);
    Task<bool> IsOwnedByAsync(string deviceCode, DeviceOperationKind operationKind, Guid operationId, CancellationToken cancellationToken = default);
    Task<bool> TryAcquireAsync(DeviceOperationHold hold, CancellationToken cancellationToken = default);
    Task<bool> ReleaseAsync(string deviceCode, DeviceOperationKind operationKind, Guid operationId, CancellationToken cancellationToken = default);
    Task<int> AttachDeviceAsync(string deviceCode, Guid deviceId, DeviceOperationKind operationKind, Guid operationId, CancellationToken cancellationToken = default);
}
public interface IDeviceReceiptRepository : IRepository<DeviceReceipt>
{
    Task<PagedResult<DeviceReceiptReadModel>> ListAsync(DeviceReceiptListQuery query, CancellationToken cancellationToken = default);
    Task<DeviceReceiptReadModel?> GetByCodeAsync(string receiptCode, CancellationToken cancellationToken = default);
    Task<DeviceReceipt?> GetByIdTrackedAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> CountForSupplierAsync(Guid supplierId, CancellationToken cancellationToken = default);
}
public interface IDeviceReceiptItemRepository : IRepository<DeviceReceiptItem>
{
    Task<IReadOnlyList<DeviceReceiptItemReadModel>> ListForReceiptAsync(Guid receiptId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeviceReceiptItem>> ListTrackedForReceiptAsync(Guid receiptId, CancellationToken cancellationToken = default);
}
public interface IDeviceReceiptHistoryRepository : IRepository<DeviceReceiptHistory>
{
    Task<PagedResult<DeviceReceiptHistoryReadModel>> ListForReceiptAsync(Guid receiptId, DeviceReceiptHistoryQuery query, CancellationToken cancellationToken = default);
}
public interface IDeviceLoanRepository : IRepository<DeviceLoan>
{
    Task<PagedResult<DeviceLoanReadModel>> ListAsync(DeviceLoanListQuery query, Guid? departmentScope, Guid actorAccountId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<DeviceLoanReadModel?> GetByCodeAsync(string loanCode, Guid? departmentScope, Guid actorAccountId, DateTimeOffset now, CancellationToken cancellationToken = default);
}
public interface IDeviceLoanItemRepository : IRepository<DeviceLoanItem>
{
    Task<IReadOnlyList<DeviceLoanItemReadModel>> ListForLoanAsync(Guid loanId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeviceLoanItem>> ListTrackedForLoanAsync(Guid loanId, CancellationToken cancellationToken = default);
}
public interface IDeviceLoanHistoryRepository : IRepository<DeviceLoanHistory>
{
    Task<PagedResult<DeviceLoanHistoryReadModel>> ListForLoanAsync(Guid loanId, DeviceLoanHistoryQuery query, CancellationToken cancellationToken = default);
}
public interface IDeviceTransferRepository : IRepository<DeviceTransfer>
{
    Task<PagedResult<DeviceTransferReadModel>> ListAsync(DeviceTransferListQuery query, Guid? departmentScope, Guid actorAccountId, CancellationToken cancellationToken = default);
    Task<DeviceTransferReadModel?> GetByCodeAsync(string transferCode, Guid? departmentScope, Guid actorAccountId, CancellationToken cancellationToken = default);
}
public interface IDeviceTransferItemRepository : IRepository<DeviceTransferItem>
{
    Task<IReadOnlyList<DeviceTransferItemReadModel>> ListForTransferAsync(Guid transferId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeviceTransferItem>> ListTrackedForTransferAsync(Guid transferId, CancellationToken cancellationToken = default);
}
public interface IDeviceTransferHistoryRepository : IRepository<DeviceTransferHistory>
{
    Task<PagedResult<DeviceTransferHistoryReadModel>> ListForTransferAsync(Guid transferId, DeviceTransferHistoryQuery query, CancellationToken cancellationToken = default);
}
