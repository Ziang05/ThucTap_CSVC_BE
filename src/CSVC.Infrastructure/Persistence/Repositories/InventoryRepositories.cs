using CSVC.Core.DeviceLoans;
using CSVC.Core.DeviceReceipts;
using CSVC.Core.DeviceTransfers;
using CSVC.Core.Devices;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Common;
using CSVC.UseCases.Devices.DTOs;
using CSVC.UseCases.Devices.Models;
using Microsoft.EntityFrameworkCore;
using CSVC.Core.Common;
using CSVC.UseCases.DeviceReceipts.Models;
using CSVC.UseCases.DeviceReceipts.DTOs;
using CSVC.UseCases.DeviceLoans.DTOs;
using CSVC.UseCases.DeviceLoans.Models;
using CSVC.UseCases.DeviceTransfers.DTOs;
using CSVC.UseCases.DeviceTransfers.Models;

namespace CSVC.Infrastructure.Persistence.Repositories;

public sealed class DeviceRepository(CsvcDbContext context) : GenericRepository<Device>(context), IDeviceRepository
{
    private IQueryable<DeviceReadModel> ReadModels()
        => from device in _context.Devices.AsNoTracking()
           join category in _context.DeviceCategories.AsNoTracking() on device.CategoryId equals category.Id
           join department in _context.Departments.AsNoTracking() on device.DepartmentId equals department.Id
           join area0 in _context.Areas.AsNoTracking() on device.AreaId equals area0.Id into areas
           from area in areas.DefaultIfEmpty()
           join account0 in _context.Accounts.AsNoTracking() on device.ResponsibleAccountId equals account0.Id into accounts
           from account in accounts.DefaultIfEmpty()
           join profile0 in _context.UserProfiles.AsNoTracking() on account!.UserProfileId equals profile0.Id into profiles
           from profile in profiles.DefaultIfEmpty()
           join manufacturer0 in _context.Manufacturers.AsNoTracking() on device.ManufacturerId equals manufacturer0.Id into manufacturers
           from manufacturer in manufacturers.DefaultIfEmpty()
           join parent0 in _context.Devices.AsNoTracking() on device.ParentDeviceId equals parent0.Id into parents
           from parent in parents.DefaultIfEmpty()
           join hold0 in _context.DeviceOperationHolds.AsNoTracking() on device.Id equals hold0.DeviceId into holds
           from hold in holds.DefaultIfEmpty()
           join loan0 in _context.DeviceLoans.AsNoTracking() on hold!.OperationId equals loan0.Id into loans
           from loan in loans.DefaultIfEmpty()
           select new DeviceReadModel
           {
               Id = device.Id, DeviceCode = device.DeviceCode, Barcode = device.Barcode, Name = device.Name,
               ParentDeviceCode = parent == null || parent.DepartmentId != device.DepartmentId ? null : parent.DeviceCode,
               ParentDeviceName = parent == null || parent.DepartmentId != device.DepartmentId ? null : parent.Name,
               HasParent = device.ParentDeviceId != null,
               CategoryCode = category.DisplayCode, CategoryName = category.Name,
               DepartmentCode = department.Code, DepartmentName = department.Name, AreaCode = area == null ? null : area.Code,
               Status = device.Status, Condition = device.Condition,
               ResponsibleAccountCode = account == null ? null : account.AccountCode,
               ResponsibleFullName = profile == null ? null : profile.FullName,
               ManufacturerCode = manufacturer == null ? null : manufacturer.DisplayCode,
               ManufacturerName = manufacturer == null ? null : manufacturer.Name,
               Model = device.Model, SerialNumber = device.SerialNumber, PurchaseDate = device.PurchaseDate,
               PaidInvoiceCode = device.PaidInvoiceCode, PurchasePrice = device.PurchasePrice,
               WarrantyExpiryDate = device.WarrantyExpiryDate, Notes = device.Notes, Version = device.Version,
               HasHold = hold != null, HoldKind = hold == null ? null : (DeviceOperationKind?)hold.OperationKind,
               IsOnLoan = hold != null && hold.OperationKind == DeviceOperationKind.Loan && loan != null && loan.Status == DeviceLoanStatus.OnLoan
           };

    public async Task<PagedResult<DeviceReadModel>> ListAsync(DeviceListQuery query, Guid? departmentScope, CancellationToken ct = default)
    {
        var source = ReadModels();
        if (!string.IsNullOrWhiteSpace(query.ParentDeviceCode))
        {
            var parentCode = query.ParentDeviceCode.Trim().ToUpperInvariant();
            source = source.Where(x => _context.Devices.Any(d => d.Id == x.Id && _context.Devices.Any(p => p.Id == d.ParentDeviceId && p.DeviceCode == parentCode)));
        }
        if (departmentScope is { } departmentId) source = source.Where(x => _context.Devices.Any(d => d.Id == x.Id && d.DepartmentId == departmentId));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = TextNormalization.Name(query.Search).ToLowerInvariant();
            source = source.Where(x => x.Name.ToLower().Contains(search) || x.DeviceCode.ToLower().Contains(search)
                || (x.SerialNumber != null && x.SerialNumber.ToLower().Contains(search)));
        }
        if (!string.IsNullOrWhiteSpace(query.CategoryCode)) source = source.Where(x => x.CategoryCode.ToLower() == query.CategoryCode.Trim().ToLower());
        if (query.Status is { } status) source = source.Where(x => x.Status == (status == DeviceStatusFilter.Active ? EntityStatus.Active : EntityStatus.Locked));
        if (query.Condition is { } condition) source = source.Where(x => x.Condition == condition);
        if (!string.IsNullOrWhiteSpace(query.DepartmentCode)) source = source.Where(x => x.DepartmentCode.ToLower() == query.DepartmentCode.Trim().ToLower());
        if (!string.IsNullOrWhiteSpace(query.AreaCode)) source = source.Where(x => x.AreaCode != null && x.AreaCode.ToLower() == query.AreaCode.Trim().ToLower());
        if (query.Availability is { } availability)
            source = availability switch
            {
                DeviceAvailability.Available => source.Where(x => !x.HasHold),
                DeviceAvailability.Reserved => source.Where(x => x.HasHold && !x.IsOnLoan && x.HoldKind != DeviceOperationKind.Transfer),
                DeviceAvailability.OnLoan => source.Where(x => x.IsOnLoan),
                _ => source.Where(x => x.HasHold && x.HoldKind == DeviceOperationKind.Transfer)
            };

        var total = await source.CountAsync(ct);
        IOrderedQueryable<DeviceReadModel> ordered = (query.SortBy, query.SortDirection) switch
        {
            (DeviceSortBy.Name, DeviceSortDirection.Desc) => source.OrderByDescending(x => x.Name),
            (DeviceSortBy.Name, _) => source.OrderBy(x => x.Name),
            (DeviceSortBy.Category, DeviceSortDirection.Desc) => source.OrderByDescending(x => x.CategoryName),
            (DeviceSortBy.Category, _) => source.OrderBy(x => x.CategoryName),
            (DeviceSortBy.Department, DeviceSortDirection.Desc) => source.OrderByDescending(x => x.DepartmentName),
            (DeviceSortBy.Department, _) => source.OrderBy(x => x.DepartmentName),
            (DeviceSortBy.Status, DeviceSortDirection.Desc) => source.OrderByDescending(x => x.Status),
            (DeviceSortBy.Status, _) => source.OrderBy(x => x.Status),
            (DeviceSortBy.DeviceCode, DeviceSortDirection.Desc) => source.OrderByDescending(x => x.DeviceCode),
            _ => source.OrderBy(x => x.DeviceCode)
        };
        var items = await ordered.ThenBy(x => x.DeviceCode).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }

    public Task<DeviceReadModel?> GetByCodeAsync(string deviceCode, Guid? departmentScope, CancellationToken ct = default)
    {
        var key = deviceCode.Trim().ToUpperInvariant();
        var source = ReadModels().Where(x => x.DeviceCode == key);
        if (departmentScope is { } id) source = source.Where(x => _context.Devices.Any(d => d.Id == x.Id && d.DepartmentId == id));
        return source.FirstOrDefaultAsync(ct);
    }

    public Task<DeviceReadModel?> GetByBarcodeAsync(string barcode, Guid? departmentScope, CancellationToken ct = default)
    {
        var key = barcode.Trim().ToUpperInvariant();
        var source = ReadModels().Where(x => x.Barcode == key);
        if (departmentScope is { } id) source = source.Where(x => _context.Devices.Any(d => d.Id == x.Id && d.DepartmentId == id));
        return source.FirstOrDefaultAsync(ct);
    }

    public Task<int> CountByCategoryAsync(Guid categoryId, CancellationToken ct = default)
        => _context.Devices.CountAsync(x => x.CategoryId == categoryId, ct);

    public Task<int> CountByManufacturerAsync(Guid manufacturerId, CancellationToken ct = default)
        => _context.Devices.CountAsync(x => x.ManufacturerId == manufacturerId, ct);

    public async Task<IReadOnlyList<Device>> GetSubtreeAsync(Guid rootId, CancellationToken ct = default)
        => await _context.Devices.FromSqlInterpolated($"""
            WITH RECURSIVE device_tree AS (
                SELECT id, parent_device_id FROM devices WHERE id = {rootId}
                UNION
                SELECT child.id, child.parent_device_id
                FROM devices child JOIN device_tree tree ON child.parent_device_id = tree.id
            ) SELECT d.* FROM devices d JOIN device_tree t ON t.id = d.id ORDER BY d.id
            """).AsNoTracking().ToListAsync(ct);

    public async Task<bool> WouldCreateParentCycleAsync(Guid childId, Guid parentId, CancellationToken ct = default)
        => await _context.Devices.FromSqlInterpolated($"""
            WITH RECURSIVE descendants AS (
                SELECT id FROM devices WHERE parent_device_id = {childId}
                UNION SELECT d.id FROM devices d JOIN descendants x ON d.parent_device_id = x.id
            ) SELECT d.* FROM devices d JOIN descendants x ON x.id = d.id WHERE d.id = {parentId} LIMIT 1
            """).AnyAsync(ct);

    public async Task<bool> HasPendingWorkflowAsync(Guid deviceId, CancellationToken ct = default)
    {
        if (await _context.DeviceTransferItems.AnyAsync(i => i.DeviceId == deviceId && _context.DeviceTransfers.Any(t => t.Id == i.TransferId && t.Status == DeviceTransferStatus.PendingApproval), ct)) return true;
        return await _context.DeviceLoanItems.AnyAsync(i => i.DeviceId == deviceId && _context.DeviceLoans.Any(l => l.Id == i.LoanId && l.Status == DeviceLoanStatus.PendingApproval), ct);
    }

    public async Task<int> CountByAreaAsync(Guid areaId, bool includeDescendants, CancellationToken ct = default)
    {
        if (!includeDescendants) return await _context.Devices.CountAsync(x => x.AreaId == areaId, ct);
        var subtree = AreaTreeQueries.SubtreeIds(_context, areaId);
        return await _context.Devices.CountAsync(x => x.AreaId != null && subtree.Contains(x.AreaId.Value), ct);
    }
}
public sealed class DeviceIdentityRepository(CsvcDbContext context) : GenericRepository<DeviceIdentity>(context), IDeviceIdentityRepository { }
public sealed class DeviceHistoryRepository(CsvcDbContext context) : GenericRepository<DeviceHistory>(context), IDeviceHistoryRepository
{
    public async Task<PagedResult<DeviceHistoryReadModel>> ListForDeviceAsync(Guid deviceId, DeviceHistoryQuery query, CancellationToken ct = default)
    {
        return await PageTimelineAsync(deviceId, query, ct);
    }

    public Task<PagedResult<DeviceHistoryReadModel>> ListTimelineForDeviceAsync(Guid deviceId, DeviceHistoryQuery query, CancellationToken ct = default)
        => PageTimelineAsync(deviceId, query, ct);

    public Task<PagedResult<DeviceHistoryReadModel>> ListGlobalTimelineAsync(DeviceHistoryQuery query, CancellationToken ct = default)
        => PageTimelineAsync(null, query, ct);

    private async Task<PagedResult<DeviceHistoryReadModel>> PageTimelineAsync(Guid? deviceId, DeviceHistoryQuery query, CancellationToken ct)
    {
        var source = TimelineSources(deviceId);
        if (!string.IsNullOrWhiteSpace(query.EventType)) source = source.Where(x => x.EventType == query.EventType.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(query.DeviceCode)) source = source.Where(x => x.DeviceCode == query.DeviceCode.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(query.DepartmentCode)) source = source.Where(x => x.DepartmentCode == query.DepartmentCode.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(query.ActorAccountCode)) source = source.Where(x => x.ActorAccountCode == query.ActorAccountCode.Trim().ToUpperInvariant());
        if (query.FromDate is { } from) source = source.Where(x => x.OccurredAt >= from);
        if (query.ToDate is { } to) source = source.Where(x => x.OccurredAt <= to);
        var total = await source.CountAsync(ct);
        var items = await source.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.EventType).ThenBy(x => x.ActionCode).ThenBy(x => x.DeviceCode)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }

    private IQueryable<DeviceHistoryReadModel> TimelineSources(Guid? deviceId)
    {
        var deviceEvents = from history in _context.DeviceHistories.AsNoTracking()
                           join device in _context.Devices.AsNoTracking() on history.DeviceId equals device.Id
                           join department in _context.Departments.AsNoTracking() on device.DepartmentId equals department.Id
                           join account0 in _context.Accounts.AsNoTracking() on history.ActorAccountId equals account0.Id into accounts
                           from account in accounts.DefaultIfEmpty()
                           join profile0 in _context.UserProfiles.AsNoTracking() on account!.UserProfileId equals profile0.Id into profiles
                           from profile in profiles.DefaultIfEmpty()
                           where deviceId == null || device.Id == deviceId
                           select new DeviceHistoryReadModel
                           {
                               EventType = "DEVICE", ActionCode = history.ActionCode, Outcome = history.Outcome, OccurredAt = history.OccurredAt,
                               DeviceCode = device.DeviceCode, DeviceName = device.Name, DepartmentCode = department.Code,
                               ActorAccountCode = account == null ? null : account.AccountCode, ActorFullName = profile == null ? null : profile.FullName,
                               ReasonCode = history.ReasonCode, ChangedFields = history.ChangedFields, BeforeData = history.BeforeData, AfterData = history.AfterData
                           };
        var receiptEvents = from history in _context.DeviceReceiptHistories.AsNoTracking()
                            join receipt in _context.DeviceReceipts.AsNoTracking() on history.ReceiptId equals receipt.Id
                            join item in _context.DeviceReceiptItems.AsNoTracking() on receipt.Id equals item.ReceiptId
                            where item.DeviceId != null
                            join device in _context.Devices.AsNoTracking() on item.DeviceId equals (Guid?)device.Id
                            join department in _context.Departments.AsNoTracking() on device.DepartmentId equals department.Id
                            join account0 in _context.Accounts.AsNoTracking() on history.ActorAccountId equals account0.Id into accounts
                            from account in accounts.DefaultIfEmpty()
                            join profile0 in _context.UserProfiles.AsNoTracking() on account!.UserProfileId equals profile0.Id into profiles
                            from profile in profiles.DefaultIfEmpty()
                            where deviceId == null || device.Id == deviceId
                            select new DeviceHistoryReadModel
                            {
                                EventType = "RECEIPT", ActionCode = history.ActionCode, Outcome = history.Outcome, OccurredAt = history.OccurredAt,
                                BusinessCode = receipt.ReceiptCode, DeviceCode = device.DeviceCode, DeviceName = device.Name, DepartmentCode = department.Code,
                                ActorAccountCode = account == null ? null : account.AccountCode, ActorFullName = profile == null ? null : profile.FullName,
                                ReasonCode = history.ReasonCode, ChangedFields = history.ChangedFields, BeforeData = history.BeforeData, AfterData = history.AfterData
                            };
        var loanEvents = from history in _context.DeviceLoanHistories.AsNoTracking()
                         join loan in _context.DeviceLoans.AsNoTracking() on history.LoanId equals loan.Id
                         join item in _context.DeviceLoanItems.AsNoTracking() on loan.Id equals item.LoanId
                         join device in _context.Devices.AsNoTracking() on item.DeviceId equals device.Id
                         join department in _context.Departments.AsNoTracking() on device.DepartmentId equals department.Id
                         join account0 in _context.Accounts.AsNoTracking() on history.ActorAccountId equals account0.Id into accounts
                         from account in accounts.DefaultIfEmpty()
                         join profile0 in _context.UserProfiles.AsNoTracking() on account!.UserProfileId equals profile0.Id into profiles
                         from profile in profiles.DefaultIfEmpty()
                         where deviceId == null || device.Id == deviceId
                         select new DeviceHistoryReadModel
                         {
                             EventType = "LOAN", ActionCode = history.ActionCode, Outcome = history.Outcome, OccurredAt = history.OccurredAt,
                             BusinessCode = loan.LoanCode, DeviceCode = device.DeviceCode, DeviceName = device.Name, DepartmentCode = department.Code,
                             ActorAccountCode = account == null ? null : account.AccountCode, ActorFullName = profile == null ? null : profile.FullName,
                             ReasonCode = history.ReasonCode, ChangedFields = history.ChangedFields, BeforeData = history.BeforeData, AfterData = history.AfterData
                         };
        var transferEvents = from history in _context.DeviceTransferHistories.AsNoTracking()
                             join transfer in _context.DeviceTransfers.AsNoTracking() on history.TransferId equals transfer.Id
                             join item in _context.DeviceTransferItems.AsNoTracking() on transfer.Id equals item.TransferId
                             join device in _context.Devices.AsNoTracking() on item.DeviceId equals device.Id
                             join department in _context.Departments.AsNoTracking() on device.DepartmentId equals department.Id
                             join account0 in _context.Accounts.AsNoTracking() on history.ActorAccountId equals account0.Id into accounts
                             from account in accounts.DefaultIfEmpty()
                             join profile0 in _context.UserProfiles.AsNoTracking() on account!.UserProfileId equals profile0.Id into profiles
                             from profile in profiles.DefaultIfEmpty()
                             where deviceId == null || device.Id == deviceId
                             select new DeviceHistoryReadModel
                             {
                                 EventType = "TRANSFER", ActionCode = history.ActionCode, Outcome = history.Outcome, OccurredAt = history.OccurredAt,
                                 BusinessCode = transfer.TransferCode, DeviceCode = device.DeviceCode, DeviceName = device.Name, DepartmentCode = department.Code,
                                 ActorAccountCode = account == null ? null : account.AccountCode, ActorFullName = profile == null ? null : profile.FullName,
                                 ReasonCode = history.ReasonCode, ChangedFields = history.ChangedFields, BeforeData = history.BeforeData, AfterData = history.AfterData
                             };
        return deviceEvents.Concat(receiptEvents).Concat(loanEvents).Concat(transferEvents);
    }
}
public sealed class DeviceReceiptRepository(CsvcDbContext context) : GenericRepository<DeviceReceipt>(context), IDeviceReceiptRepository
{
    private IQueryable<DeviceReceiptReadModel> ReadModels()
        => from receipt in _context.DeviceReceipts.AsNoTracking()
           join supplier in _context.Suppliers.AsNoTracking() on receipt.SupplierId equals supplier.Id
           join department in _context.Departments.AsNoTracking() on receipt.RequestingDepartmentId equals department.Id
           join creator in _context.Accounts.AsNoTracking() on receipt.CreatorAccountId equals creator.Id
           join decision0 in _context.Accounts.AsNoTracking() on receipt.DecisionAccountId equals decision0.Id into decisions
           from decision in decisions.DefaultIfEmpty()
           select new DeviceReceiptReadModel
           {
               Id = receipt.Id, ReceiptCode = receipt.ReceiptCode, SupplierCode = supplier.DisplayCode, SupplierName = supplier.Name,
               CarrierName = receipt.CarrierName, RequestCode = receipt.RequestCode, DeliveryDate = receipt.DeliveryDate,
               DeliveryBatch = receipt.DeliveryBatch, DepartmentCode = department.Code, DepartmentName = department.Name,
               PaidInvoiceCode = receipt.PaidInvoiceCode, Status = receipt.Status, CreatorAccountCode = creator.AccountCode,
               CreatedAt = receipt.CreatedAt, DecisionAccountCode = decision == null ? null : decision.AccountCode,
               DecidedAt = receipt.DecidedAt, CompletedAt = receipt.CompletedAt, DecisionReason = receipt.DecisionReason,
               Notes = receipt.Notes, ItemCount = _context.DeviceReceiptItems.Count(i => i.ReceiptId == receipt.Id), Version = receipt.Version
           };

    public async Task<PagedResult<DeviceReceiptReadModel>> ListAsync(DeviceReceiptListQuery query, CancellationToken ct = default)
    {
        var source = ReadModels();
        if (!string.IsNullOrWhiteSpace(query.ReceiptCode)) source = source.Where(x => x.ReceiptCode.ToLower().Contains(query.ReceiptCode.Trim().ToLower()));
        if (!string.IsNullOrWhiteSpace(query.SupplierCode)) source = source.Where(x => x.SupplierCode.ToLower() == query.SupplierCode.Trim().ToLower());
        if (!string.IsNullOrWhiteSpace(query.RequestingDepartmentCode)) source = source.Where(x => x.DepartmentCode.ToLower() == query.RequestingDepartmentCode.Trim().ToLower());
        if (!string.IsNullOrWhiteSpace(query.CreatedByAccountCode)) source = source.Where(x => x.CreatorAccountCode.ToLower() == query.CreatedByAccountCode.Trim().ToLower());
        if (query.Status is { } status) source = source.Where(x => x.Status == status);
        if (query.FromDate is { } from) source = source.Where(x => x.DeliveryDate >= from);
        if (query.ToDate is { } to) source = source.Where(x => x.DeliveryDate <= to);
        var total = await source.CountAsync(ct);
        var items = await source.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.ReceiptCode)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }

    public Task<DeviceReceiptReadModel?> GetByCodeAsync(string receiptCode, CancellationToken ct = default)
        => ReadModels().FirstOrDefaultAsync(x => x.ReceiptCode == receiptCode.Trim().ToUpperInvariant(), ct);

    public Task<DeviceReceipt?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default)
        => _dbSet.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<int> CountForSupplierAsync(Guid supplierId, CancellationToken ct = default)
        => _dbSet.CountAsync(x => x.SupplierId == supplierId, ct);
}
public sealed class DeviceReceiptItemRepository(CsvcDbContext context) : GenericRepository<DeviceReceiptItem>(context), IDeviceReceiptItemRepository
{
    public async Task<IReadOnlyList<DeviceReceiptItem>> ListTrackedForReceiptAsync(Guid receiptId, CancellationToken ct = default)
        => await _dbSet.Where(x => x.ReceiptId == receiptId).OrderBy(x => x.DeviceCode).ToListAsync(ct);

    public async Task<IReadOnlyList<DeviceReceiptItemReadModel>> ListForReceiptAsync(Guid receiptId, CancellationToken ct = default)
        => await (from item in _context.DeviceReceiptItems.AsNoTracking()
                  join category in _context.DeviceCategories.AsNoTracking() on item.CategoryId equals category.Id
                  where item.ReceiptId == receiptId
                  orderby category.DisplayCode, item.DeviceCode
                  select new DeviceReceiptItemReadModel
                  {
                      Id = item.Id, CategoryCode = category.DisplayCode, CategoryName = category.Name, Name = item.Name,
                      DeviceCode = item.DeviceCode, Barcode = item.Barcode, DeviceCodeSource = item.DeviceCodeSource,
                      SerialNumber = item.SerialNumber, Completed = item.DeviceId != null, ParentDeviceCode = item.ParentDeviceCode
                  }).ToListAsync(ct);
}
public sealed class DeviceReceiptHistoryRepository(CsvcDbContext context) : GenericRepository<DeviceReceiptHistory>(context), IDeviceReceiptHistoryRepository
{
    public async Task<PagedResult<DeviceReceiptHistoryReadModel>> ListForReceiptAsync(Guid receiptId, DeviceReceiptHistoryQuery query, CancellationToken ct = default)
    {
        var source = from history in _context.DeviceReceiptHistories.AsNoTracking()
                     join account0 in _context.Accounts.AsNoTracking() on history.ActorAccountId equals account0.Id into accounts
                     from account in accounts.DefaultIfEmpty()
                     join profile0 in _context.UserProfiles.AsNoTracking() on account!.UserProfileId equals profile0.Id into profiles
                     from profile in profiles.DefaultIfEmpty()
                     where history.ReceiptId == receiptId
                     select new DeviceReceiptHistoryReadModel
                     {
                         ActionCode = history.ActionCode, Outcome = history.Outcome, OccurredAt = history.OccurredAt,
                         ActorAccountCode = account == null ? null : account.AccountCode, ActorFullName = profile == null ? null : profile.FullName,
                         ReasonCode = history.ReasonCode, ChangedFields = history.ChangedFields,
                         BeforeData = history.BeforeData, AfterData = history.AfterData
                     };
        var total = await source.CountAsync(ct);
        var items = await source.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.ActionCode)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }
}
public sealed class DeviceLoanRepository(CsvcDbContext context) : GenericRepository<DeviceLoan>(context), IDeviceLoanRepository
{
    private IQueryable<DeviceLoanReadModel> ReadModels(DateTimeOffset now)
        => from loan in _context.DeviceLoans.AsNoTracking()
           join borrower in _context.Accounts.AsNoTracking() on loan.BorrowerAccountId equals borrower.Id
           join borrowerProfile in _context.UserProfiles.AsNoTracking() on borrower.UserProfileId equals borrowerProfile.Id
           join department in _context.Departments.AsNoTracking() on loan.BorrowerDepartmentId equals department.Id
           join creator in _context.Accounts.AsNoTracking() on loan.CreatorAccountId equals creator.Id
           join decision0 in _context.Accounts.AsNoTracking() on loan.DecisionAccountId equals decision0.Id into decisions
           from decision in decisions.DefaultIfEmpty()
           join handover0 in _context.Accounts.AsNoTracking() on loan.HandoverAccountId equals handover0.Id into handovers
           from handover in handovers.DefaultIfEmpty()
           join return0 in _context.Accounts.AsNoTracking() on loan.ReturnAccountId equals return0.Id into returns
           from returnAccount in returns.DefaultIfEmpty()
           select new DeviceLoanReadModel
           {
               Id = loan.Id, LoanCode = loan.LoanCode, BorrowerAccountCode = borrower.AccountCode,
               BorrowerFullName = borrowerProfile.FullName, DepartmentCode = department.Code, DepartmentName = department.Name,
               Purpose = loan.Purpose, DueAt = loan.DueAt, Status = loan.Status, CreatorAccountCode = creator.AccountCode,
               CreatedAt = loan.CreatedAt, DecisionAccountCode = decision == null ? null : decision.AccountCode,
               DecidedAt = loan.DecidedAt, DecisionReason = loan.DecisionReason, HandedOverAt = loan.HandedOverAt,
               HandoverAccountCode = handover == null ? null : handover.AccountCode, ReturnedAt = loan.ReturnedAt,
               ReturnAccountCode = returnAccount == null ? null : returnAccount.AccountCode, Notes = loan.Notes,
               ItemCount = _context.DeviceLoanItems.Count(x => x.LoanId == loan.Id), Version = loan.Version,
               IsOverdue = loan.Status == DeviceLoanStatus.OnLoan && loan.DueAt < now
           };

    public async Task<PagedResult<DeviceLoanReadModel>> ListAsync(DeviceLoanListQuery query, Guid? departmentScope, Guid actorAccountId, DateTimeOffset now, CancellationToken ct = default)
    {
        var source = ReadModels(now);
        if (departmentScope is { } departmentId)
            source = source.Where(x => _context.DeviceLoans.Any(l => l.Id == x.Id &&
                (l.BorrowerDepartmentId == departmentId || l.BorrowerAccountId == actorAccountId || l.CreatorAccountId == actorAccountId)));
        if (!string.IsNullOrWhiteSpace(query.LoanCode)) source = source.Where(x => x.LoanCode.ToLower().Contains(query.LoanCode.Trim().ToLower()));
        if (!string.IsNullOrWhiteSpace(query.BorrowerAccountCode)) source = source.Where(x => x.BorrowerAccountCode.ToLower() == query.BorrowerAccountCode.Trim().ToLower());
        if (!string.IsNullOrWhiteSpace(query.DepartmentCode)) source = source.Where(x => x.DepartmentCode.ToLower() == query.DepartmentCode.Trim().ToLower());
        if (query.Status is { } status) source = source.Where(x => x.Status == status);
        if (query.Overdue == true) source = source.Where(x => x.IsOverdue);
        if (query.Overdue == false) source = source.Where(x => !x.IsOverdue);
        var total = await source.CountAsync(ct);
        var items = await source.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.LoanCode)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }

    public Task<DeviceLoanReadModel?> GetByCodeAsync(string loanCode, Guid? departmentScope, Guid actorAccountId, DateTimeOffset now, CancellationToken ct = default)
    {
        var source = ReadModels(now).Where(x => x.LoanCode == loanCode.Trim().ToUpperInvariant());
        if (departmentScope is { } departmentId)
            source = source.Where(x => _context.DeviceLoans.Any(l => l.Id == x.Id &&
                (l.BorrowerDepartmentId == departmentId || l.BorrowerAccountId == actorAccountId || l.CreatorAccountId == actorAccountId)));
        return source.FirstOrDefaultAsync(ct);
    }
}
public sealed class DeviceLoanItemRepository(CsvcDbContext context) : GenericRepository<DeviceLoanItem>(context), IDeviceLoanItemRepository
{
    public async Task<IReadOnlyList<DeviceLoanItem>> ListTrackedForLoanAsync(Guid loanId, CancellationToken ct = default)
        => await _dbSet.Where(x => x.LoanId == loanId).OrderBy(x => x.DeviceId).ToListAsync(ct);

    public async Task<IReadOnlyList<DeviceLoanItemReadModel>> ListForLoanAsync(Guid loanId, CancellationToken ct = default)
        => await (from item in _context.DeviceLoanItems.AsNoTracking()
                  join device in _context.Devices.AsNoTracking() on item.DeviceId equals device.Id
                  join category in _context.DeviceCategories.AsNoTracking() on device.CategoryId equals category.Id
                  where item.LoanId == loanId
                  orderby device.DeviceCode
                  select new DeviceLoanItemReadModel
                  {
                      DeviceCode = device.DeviceCode, Name = device.Name, CategoryCode = category.DisplayCode, CategoryName = category.Name,
                      ConditionAtHandover = item.ConditionAtHandover, HandoverNote = item.HandoverNote,
                      ConditionAtReturn = item.ConditionAtReturn, ReturnNote = item.ReturnNote
                  }).ToListAsync(ct);
}
public sealed class DeviceLoanHistoryRepository(CsvcDbContext context) : GenericRepository<DeviceLoanHistory>(context), IDeviceLoanHistoryRepository
{
    public async Task<PagedResult<DeviceLoanHistoryReadModel>> ListForLoanAsync(Guid loanId, DeviceLoanHistoryQuery query, CancellationToken ct = default)
    {
        var source = from history in _context.DeviceLoanHistories.AsNoTracking()
                     join account0 in _context.Accounts.AsNoTracking() on history.ActorAccountId equals account0.Id into accounts
                     from account in accounts.DefaultIfEmpty()
                     join profile0 in _context.UserProfiles.AsNoTracking() on account!.UserProfileId equals profile0.Id into profiles
                     from profile in profiles.DefaultIfEmpty()
                     where history.LoanId == loanId
                     select new DeviceLoanHistoryReadModel
                     {
                         ActionCode = history.ActionCode, Outcome = history.Outcome, OccurredAt = history.OccurredAt,
                         ActorAccountCode = account == null ? null : account.AccountCode, ActorFullName = profile == null ? null : profile.FullName,
                         ReasonCode = history.ReasonCode, ChangedFields = history.ChangedFields,
                         BeforeData = history.BeforeData, AfterData = history.AfterData
                     };
        var total = await source.CountAsync(ct);
        var items = await source.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.ActionCode)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }
}
public sealed class DeviceTransferRepository(CsvcDbContext context) : GenericRepository<DeviceTransfer>(context), IDeviceTransferRepository
{
    private IQueryable<DeviceTransferReadModel> ReadModels()
        => from transfer in _context.DeviceTransfers.AsNoTracking()
           join source in _context.Departments.AsNoTracking() on transfer.SourceDepartmentId equals source.Id
           join target in _context.Departments.AsNoTracking() on transfer.TargetDepartmentId equals target.Id
           join responsible in _context.Accounts.AsNoTracking() on transfer.TargetResponsibleAccountId equals responsible.Id
           join profile in _context.UserProfiles.AsNoTracking() on responsible.UserProfileId equals profile.Id
           join creator in _context.Accounts.AsNoTracking() on transfer.CreatorAccountId equals creator.Id
           join decision0 in _context.Accounts.AsNoTracking() on transfer.DecisionAccountId equals decision0.Id into decisions
           from decision in decisions.DefaultIfEmpty()
           join sourceHandover0 in _context.Accounts.AsNoTracking() on transfer.SourceHandoverAccountId equals sourceHandover0.Id into sourceHandovers
           from sourceHandover in sourceHandovers.DefaultIfEmpty()
           join targetHandover0 in _context.Accounts.AsNoTracking() on transfer.TargetHandoverAccountId equals targetHandover0.Id into targetHandovers
           from targetHandover in targetHandovers.DefaultIfEmpty()
           select new DeviceTransferReadModel
           {
               Id = transfer.Id, TransferCode = transfer.TransferCode,
               SourceDepartmentCode = source.Code, SourceDepartmentName = source.Name,
               TargetDepartmentCode = target.Code, TargetDepartmentName = target.Name,
               TargetResponsibleAccountCode = responsible.AccountCode, TargetResponsibleFullName = profile.FullName,
               Reason = transfer.Reason, Status = transfer.Status, CreatorAccountCode = creator.AccountCode,
               CreatedAt = transfer.CreatedAt, DecisionAccountCode = decision == null ? null : decision.AccountCode,
               DecidedAt = transfer.DecidedAt, DecisionReason = transfer.DecisionReason,
               SourceHandoverAccountCode = sourceHandover == null ? null : sourceHandover.AccountCode,
               TargetHandoverAccountCode = targetHandover == null ? null : targetHandover.AccountCode,
               CompletedAt = transfer.CompletedAt, Notes = transfer.Notes,
               ItemCount = _context.DeviceTransferItems.Count(x => x.TransferId == transfer.Id), Version = transfer.Version
           };

    public async Task<PagedResult<DeviceTransferReadModel>> ListAsync(DeviceTransferListQuery query, Guid? departmentScope, Guid actorAccountId, CancellationToken ct = default)
    {
        var source = ReadModels();
        if (departmentScope is { } departmentId)
            source = source.Where(x => _context.DeviceTransfers.Any(t => t.Id == x.Id &&
                (t.SourceDepartmentId == departmentId || t.TargetDepartmentId == departmentId || t.CreatorAccountId == actorAccountId)));
        if (!string.IsNullOrWhiteSpace(query.TransferCode)) source = source.Where(x => x.TransferCode.ToLower().Contains(query.TransferCode.Trim().ToLower()));
        if (!string.IsNullOrWhiteSpace(query.DepartmentCode))
        {
            var departmentCode = query.DepartmentCode.Trim().ToLower();
            source = source.Where(x => x.SourceDepartmentCode.ToLower() == departmentCode || x.TargetDepartmentCode.ToLower() == departmentCode);
        }
        if (query.Status is { } status) source = source.Where(x => x.Status == status);
        if (query.FromDate is { } from)
        {
            var fromTime = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            source = source.Where(x => x.CreatedAt >= fromTime);
        }
        if (query.ToDate is { } to)
        {
            if (to == DateOnly.MaxValue)
                source = source.Where(x => x.CreatedAt <= new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc)));
            else
            {
                var toExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                source = source.Where(x => x.CreatedAt < toExclusive);
            }
        }
        var total = await source.CountAsync(ct);
        var items = await source.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.TransferCode)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }

    public Task<DeviceTransferReadModel?> GetByCodeAsync(string transferCode, Guid? departmentScope, Guid actorAccountId, CancellationToken ct = default)
    {
        var source = ReadModels().Where(x => x.TransferCode == transferCode.Trim().ToUpperInvariant());
        if (departmentScope is { } departmentId)
            source = source.Where(x => _context.DeviceTransfers.Any(t => t.Id == x.Id &&
                (t.SourceDepartmentId == departmentId || t.TargetDepartmentId == departmentId || t.CreatorAccountId == actorAccountId)));
        return source.FirstOrDefaultAsync(ct);
    }
}
public sealed class DeviceTransferItemRepository(CsvcDbContext context) : GenericRepository<DeviceTransferItem>(context), IDeviceTransferItemRepository
{
    public async Task<IReadOnlyList<DeviceTransferItem>> ListTrackedForTransferAsync(Guid transferId, CancellationToken ct = default)
        => await _dbSet.Where(x => x.TransferId == transferId).OrderBy(x => x.DeviceId).ToListAsync(ct);

    public async Task<IReadOnlyList<DeviceTransferItemReadModel>> ListForTransferAsync(Guid transferId, CancellationToken ct = default)
        => await (from item in _context.DeviceTransferItems.AsNoTracking()
                  join device in _context.Devices.AsNoTracking() on item.DeviceId equals device.Id
                  join category in _context.DeviceCategories.AsNoTracking() on device.CategoryId equals category.Id
                  where item.TransferId == transferId
                  orderby device.DeviceCode
                  select new DeviceTransferItemReadModel
                  {
                      DeviceCode = device.DeviceCode, Name = device.Name, CategoryCode = category.DisplayCode, CategoryName = category.Name,
                      ConditionAtHandover = item.ConditionAtHandover, HandoverNote = item.HandoverNote
                  }).ToListAsync(ct);
}
public sealed class DeviceTransferHistoryRepository(CsvcDbContext context) : GenericRepository<DeviceTransferHistory>(context), IDeviceTransferHistoryRepository
{
    public async Task<PagedResult<DeviceTransferHistoryReadModel>> ListForTransferAsync(Guid transferId, DeviceTransferHistoryQuery query, CancellationToken ct = default)
    {
        var source = from history in _context.DeviceTransferHistories.AsNoTracking()
                     join account0 in _context.Accounts.AsNoTracking() on history.ActorAccountId equals account0.Id into accounts
                     from account in accounts.DefaultIfEmpty()
                     join profile0 in _context.UserProfiles.AsNoTracking() on account!.UserProfileId equals profile0.Id into profiles
                     from profile in profiles.DefaultIfEmpty()
                     where history.TransferId == transferId
                     select new DeviceTransferHistoryReadModel
                     {
                         ActionCode = history.ActionCode, Outcome = history.Outcome, OccurredAt = history.OccurredAt,
                         ActorAccountCode = account == null ? null : account.AccountCode, ActorFullName = profile == null ? null : profile.FullName,
                         ReasonCode = history.ReasonCode, ChangedFields = history.ChangedFields,
                         BeforeData = history.BeforeData, AfterData = history.AfterData
                     };
        var total = await source.CountAsync(ct);
        var items = await source.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.ActionCode)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }
}
