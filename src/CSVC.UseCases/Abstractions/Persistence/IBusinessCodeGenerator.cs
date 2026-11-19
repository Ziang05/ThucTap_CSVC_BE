namespace CSVC.UseCases.Abstractions.Persistence;

public interface IBusinessCodeGenerator
{
    // Called inside the corresponding write transaction; allocated numbers may have gaps.
    Task<string> NextAccountCodeAsync(CancellationToken cancellationToken);
    Task<string> NextDepartmentCodeAsync(CancellationToken cancellationToken);
    Task<string> NextAsync(BusinessCodeKind kind, CancellationToken cancellationToken, IReadOnlySet<string>? excludedCodes = null);
}

public enum BusinessCodeKind
{
    Area,
    DeviceCategory,
    Manufacturer,
    Supplier,
    Device,
    DeviceReceipt,
    DeviceLoan,
    DeviceTransfer,
    DeviceRecovery,
    DamageReport,
    MaintenanceOrder,
    RepairOrder,
    ExportSlip,
    ExportReturnSlip,
    InventorySlip
}
