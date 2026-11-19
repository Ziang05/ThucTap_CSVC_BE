using CSVC.Core.ServiceOrders;
using CSVC.UseCases.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence;

public sealed class BusinessCodeGenerator(CsvcDbContext context, TimeProvider clock) : IBusinessCodeGenerator
{
    internal const string SequenceName = "business_code_seq";
    private static readonly TimeSpan VietnamUtcOffset = TimeSpan.FromHours(7);

    public async Task<string> NextAsync(BusinessCodeKind kind, CancellationToken cancellationToken, IReadOnlySet<string>? excludedCodes = null)
    {
        var prefix = kind switch
        {
            BusinessCodeKind.Account => "TK",
            BusinessCodeKind.Department => "PB",
            BusinessCodeKind.Area => "KV",
            BusinessCodeKind.DeviceCategory => "DM",
            BusinessCodeKind.Manufacturer => "HSX",
            BusinessCodeKind.Supplier => "NCC",
            BusinessCodeKind.Device => "TB",
            BusinessCodeKind.DeviceReceipt => "PN",
            BusinessCodeKind.DeviceLoan => "MT",
            BusinessCodeKind.DeviceTransfer => "DC",
            BusinessCodeKind.DeviceRecovery => "TH",
            BusinessCodeKind.DamageReport => "BH",
            BusinessCodeKind.MaintenanceOrder => "BT",
            BusinessCodeKind.RepairOrder => "SC",
            BusinessCodeKind.ExportSlip => "PX",
            BusinessCodeKind.ExportReturnSlip => "NL",
            BusinessCodeKind.InventorySlip => "KK",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var date = clock.GetUtcNow().ToOffset(VietnamUtcOffset);
        while (true)
        {
            var number = await context.Database.SqlQuery<long>(
                $"SELECT nextval({SequenceName}::regclass) AS \"Value\"").SingleAsync(cancellationToken);
            var code = FormattableString.Invariant($"{prefix}-{date:yyyyMMdd}-{number:D4}");
            if (excludedCodes?.Contains(code) != true && !await ExistsAsync(kind, code, cancellationToken))
                return code;
        }
    }

    private Task<bool> ExistsAsync(BusinessCodeKind kind, string code, CancellationToken ct)
    {
        var normalized = code.ToLowerInvariant();
        return kind switch
        {
            BusinessCodeKind.Account => context.Accounts.AnyAsync(x => EF.Property<string>(x, "NormalizedCode") == normalized, ct),
            BusinessCodeKind.Department => context.Departments.AnyAsync(x => EF.Property<string>(x, "NormalizedCode") == normalized, ct),
            BusinessCodeKind.Area => context.Areas.AnyAsync(x => x.Code == code, ct),
            BusinessCodeKind.DeviceCategory => CatalogCodeExistsAsync(context.DeviceCategories, normalized, ct),
            BusinessCodeKind.Manufacturer => CatalogCodeExistsAsync(context.Manufacturers, normalized, ct),
            BusinessCodeKind.Supplier => CatalogCodeExistsAsync(context.Suppliers, normalized, ct),
            BusinessCodeKind.Device => context.DeviceIdentities.AnyAsync(x => x.DeviceCode == code, ct),
            BusinessCodeKind.DeviceReceipt => context.DeviceReceipts.AnyAsync(x => x.ReceiptCode == code, ct),
            BusinessCodeKind.DeviceLoan => context.DeviceLoans.AnyAsync(x => x.LoanCode == code, ct),
            BusinessCodeKind.DeviceTransfer or BusinessCodeKind.DeviceRecovery => context.DeviceTransfers.AnyAsync(x => x.TransferCode == code, ct),
            BusinessCodeKind.DamageReport => context.DamageReports.AnyAsync(x => x.ReportCode == code, ct),
            BusinessCodeKind.MaintenanceOrder or BusinessCodeKind.RepairOrder => context.ServiceOrders.AnyAsync(x => x.DisplayCode == code, ct),
            BusinessCodeKind.ExportSlip => context.ExportSlips.AnyAsync(x => x.DisplayCode == code, ct),
            BusinessCodeKind.ExportReturnSlip => context.Set<ExportReturnSlip>().AnyAsync(x => x.DisplayCode == code, ct),
            BusinessCodeKind.InventorySlip => context.InventorySlips.AnyAsync(x => x.DisplayCode == code, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static Task<bool> CatalogCodeExistsAsync<T>(DbSet<T> entries, string normalized, CancellationToken ct) where T : class
        => entries.AnyAsync(x => EF.Property<string>(x, "NormalizedDisplayCode") == normalized, ct);
}
