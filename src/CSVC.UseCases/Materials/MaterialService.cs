using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CSVC.Core.Common;
using CSVC.Core.Materials;
using CSVC.UseCases.Abstractions.Persistence;
using CSVC.UseCases.Common;
using CSVC.UseCases.Devices;
using CSVC.UseCases.DeviceTransfers;
using CSVC.UseCases.ProcurementRequests;
using CSVC.UseCases.Settings;

namespace CSVC.UseCases.Materials;

public sealed partial class MaterialService(MaterialAccess access, IMaterialStore store, IUnitOfWork uow,
    IRepository<MaterialUnit> units, IRepository<MaterialItem> materials, IRepository<MaterialWarehouse> warehouses,
    IRepository<MaterialLocation> locations, IRepository<MaterialBalance> balances, IRepository<MaterialDocument> documents,
    IRepository<MaterialDocumentLine> documentLines, IRepository<MaterialLedgerEntry> ledger,
    IRepository<MaterialDocumentHistory> documentHistory, IRepository<MaterialCatalogHistory> catalogHistory,
    IRepository<EquipmentChange> changes,
    IRepository<MaterialWorkUse> workUses,
    IInventoryTransaction transaction, IServiceOrderRepository services,
    IProcurementRequestRepository requests, IRepository<CSVC.Core.ProcurementRequests.ProcurementRequest> requestRecords,
    IRepository<CSVC.Core.ProcurementRequests.ProcurementDocumentLink> requestLinks,
    IDeviceReservationPolicy reservations, IDeviceTransferService transferService, IDeviceService deviceService, MaterialDeliveryScope deliveryScope,
    IRepository<CSVC.Core.ServiceOrders.ServiceOrderDeviceHold> serviceHolds, IRepository<CSVC.Core.ServiceOrders.ExportDeviceGuard> exportGuards,
    TimeProvider clock) : IMaterialService
{
    private static void RequireRecorder(MaterialActor actor) { if (!actor.Staff) throw MaterialErrors.Forbidden("Only QTTB employees may record material operations."); }
    private static string Code(string? value) => BusinessCode.Normalize(value) ?? "";
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Normalize();
    private static string Name(string value)
    { var result = TextNormalization.Name(value); if (result.Length == 0) throw MaterialErrors.Invalid("A nonempty name is required."); return result; }
    private static string NewCode(string? value, string prefix)
    {
        var code = Clean(value) is null ? CSVC.UseCases.Common.FeatureCode.New(prefix) : Code(value);
        if (code.Length > BusinessCode.MaxLength || !Regex.IsMatch(code, BusinessCode.Pattern)) throw MaterialErrors.Invalid("Invalid material code.");
        return code;
    }
    private static void Validate(object input)
    { if (!Validator.TryValidateObject(input, new ValidationContext(input), [], true)) throw MaterialErrors.Invalid("Invalid material fields."); }
    private static void Query(MaterialQuery q)
    { Validate(q); if (q.From > q.To) throw MaterialErrors.Invalid("from must not be after to."); }
    private static void Version(int actual, int? expected)
    { if (expected is null || actual != expected) throw MaterialErrors.Conflict("VERSION_CONFLICT", "Reload the current material record before saving."); }
    private static void ImmutableCode(string actual, string? input)
    { if (Clean(input) is not null && actual != Code(input)) throw MaterialErrors.Invalid("Material catalog codes are immutable."); }
    private static void Quantity(decimal quantity, int decimals, bool signed = false)
    {
        if (quantity == 0 || (!signed && quantity < 0) || Math.Abs(quantity) > 1000000000m || decimal.Round(quantity, decimals) != quantity)
            throw MaterialErrors.Invalid($"Quantity must be nonzero, within limits and use at most {decimals} decimal places for this unit.");
    }
    private static string Hash<T>(T input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ProcurementJson.Write(input))));
    private static void RequestId(Guid id) { if (id == Guid.Empty) throw MaterialErrors.Invalid("A nonempty requestId is required."); }
    private async Task CatalogAuditAsync(string catalog, Guid id, string code, MaterialActor actor, string action, object? before, object after, CancellationToken ct)
        => await catalogHistory.AddAsync(new MaterialCatalogHistory { Catalog = catalog, TargetId = id, Code = code, ActorAccountId = actor.Id, Action = action,
            BeforeData = before is null ? null : ProcurementJson.Write(before), AfterData = ProcurementJson.Write(after), OccurredAt = clock.GetUtcNow() }, ct);
    public async Task<PagedResult<UnitDto>> UnitsAsync(MaterialQuery q, CancellationToken ct) { await access.RequireAsync(ct); Query(q); return await store.UnitsAsync(q, ct); }
    public async Task<PagedResult<MaterialDto>> MaterialsAsync(MaterialQuery q, CancellationToken ct) { await access.RequireAsync(ct); Query(q); return await store.MaterialsAsync(q, ct); }
    public async Task<PagedResult<WarehouseDto>> WarehousesAsync(MaterialQuery q, CancellationToken ct) { var actor = await access.RequireAsync(ct); Query(q); return await store.WarehousesAsync(q, actor.DepartmentId, ct); }
    public async Task<PagedResult<LocationDto>> LocationsAsync(MaterialQuery q, CancellationToken ct) { var actor = await access.RequireAsync(ct); Query(q); return await store.LocationsAsync(q, actor.DepartmentId, ct); }
    public async Task<PagedResult<BalanceDto>> BalancesAsync(MaterialQuery q, CancellationToken ct) { var actor = await access.RequireAsync(ct); Query(q); return await store.BalancesAsync(q, actor.DepartmentId, ct); }
    public async Task<PagedResult<MaterialReportRow>> ReportAsync(MaterialQuery q, CancellationToken ct) { var actor = await access.RequireAsync(ct); Query(q); return await store.ReportAsync(q, actor.DepartmentId, ct); }
    public async Task<PagedResult<MaterialMovementDto>> MovementsAsync(MaterialQuery q, CancellationToken ct) { var actor = await access.RequireAsync(ct); Query(q); return await store.MovementsAsync(q, actor.DepartmentId, ct); }

    public Task<UnitDto> SaveUnitAsync(string? code, UnitWrite input, CancellationToken ct) => transaction.ExecuteAsync(async token =>
    {
        var actor = await access.RequireAsync(token, manager: true); Validate(input);
        MaterialUnit entity; object? before = null;
        if (code is null) { entity = new() { Code = NewCode(input.Code, "DV"), Name = Name(input.Name), DecimalPlaces = input.DecimalPlaces, Status = input.Status }; await units.AddAsync(entity, token); }
        else
        {
            entity = await units.FirstOrDefaultTrackedAsync(x => x.Code == Code(code), token) ?? throw MaterialErrors.Missing();
            Version(entity.Version, input.Version); ImmutableCode(entity.Code, input.Code); before = new { entity.Name, entity.DecimalPlaces, entity.Status, entity.Version };
            if (input.DecimalPlaces != entity.DecimalPlaces && await materials.ExistsAsync(m => m.UnitId == entity.Id, token)) throw MaterialErrors.Conflict("MATERIAL_UNIT_IN_USE", "A unit already used by material items cannot change precision.");
            entity.Name = Name(input.Name); entity.DecimalPlaces = input.DecimalPlaces; entity.Status = input.Status; entity.Version++;
        }
        await CatalogAuditAsync("Unit", entity.Id, entity.Code, actor, code is null ? "CATALOG_CREATED" : "CATALOG_UPDATED", before, new { entity.Name, entity.DecimalPlaces, entity.Status, entity.Version }, token);
        return new UnitDto(entity.Code, entity.Name, entity.DecimalPlaces, entity.Status, entity.Version);
    }, ct);
    public Task<MaterialDto> SaveMaterialAsync(string? code, MaterialWrite input, CancellationToken ct) => transaction.ExecuteAsync(async token =>
    {
        var actor = await access.RequireAsync(token, manager: true); Validate(input);
        var unit = await units.FirstOrDefaultAsync(u => u.Code == Code(input.UnitCode), token) ?? throw MaterialErrors.Missing("Unit not found.");
        if (input.Status == EntityStatus.Active && unit.Status != EntityStatus.Active) throw MaterialErrors.Invalid("Select an active unit.");
        MaterialItem entity; object? before = null;
        if (code is null) { entity = new() { Code = NewCode(input.Code, "VT"), Name = Name(input.Name), UnitId = unit.Id, Kind = input.Kind, Notes = Clean(input.Notes), Status = input.Status }; await materials.AddAsync(entity, token); }
        else
        {
            entity = await materials.FirstOrDefaultTrackedAsync(x => x.Code == Code(code), token) ?? throw MaterialErrors.Missing();
            Version(entity.Version, input.Version); ImmutableCode(entity.Code, input.Code); before = new { entity.Name, entity.UnitId, entity.Kind, entity.Notes, entity.Status, entity.Version };
            if ((entity.UnitId != unit.Id || entity.Kind != input.Kind) && await documentLines.ExistsAsync(l => l.MaterialId == entity.Id, token)) throw MaterialErrors.Conflict("MATERIAL_ALREADY_REFERENCED", "An item already referenced by documents cannot change its unit or material/tool classification.");
            entity.Name = Name(input.Name); entity.UnitId = unit.Id; entity.Kind = input.Kind; entity.Notes = Clean(input.Notes); entity.Status = input.Status; entity.Version++;
        }
        await CatalogAuditAsync("Material", entity.Id, entity.Code, actor, code is null ? "CATALOG_CREATED" : "CATALOG_UPDATED", before, new { entity.Name, entity.UnitId, entity.Kind, entity.Notes, entity.Status, entity.Version }, token);
        return new MaterialDto(entity.Code, entity.Name, entity.Kind, unit.Code, unit.Name, unit.DecimalPlaces, entity.Status, entity.Notes, entity.Version);
    }, ct);
    public Task<WarehouseDto> SaveWarehouseAsync(string? code, WarehouseWrite input, CancellationToken ct) => transaction.ExecuteAsync(async token =>
    {
        var actor = await access.RequireAsync(token, manager: true); Validate(input);
        MaterialWarehouse entity; object? before = null;
        if (code is null)
        {
            entity = new() { Code = NewCode(input.Code, "KM"), Name = Name(input.Name), DepartmentId = actor.DepartmentId, Notes = Clean(input.Notes), Status = input.Status };
            await warehouses.AddAsync(entity, token);
            var location = new MaterialLocation { Code = CSVC.UseCases.Common.FeatureCode.New("VK"), Name = "Vị trí chung", WarehouseId = entity.Id, Status = input.Status };
            await locations.AddAsync(location, token);
            await CatalogAuditAsync("Location", location.Id, location.Code, actor, "CATALOG_CREATED", null, new { location.Name, warehouseCode = entity.Code, location.Status }, token);
        }
        else
        {
            entity = await warehouses.FirstOrDefaultTrackedAsync(x => x.Code == Code(code) && x.DepartmentId == actor.DepartmentId, token) ?? throw MaterialErrors.Missing();
            Version(entity.Version, input.Version); ImmutableCode(entity.Code, input.Code); before = new { entity.Name, entity.Notes, entity.Status, entity.Version };
            entity.Name = Name(input.Name); entity.Notes = Clean(input.Notes); entity.Status = input.Status; entity.Version++;
        }
        await CatalogAuditAsync("Warehouse", entity.Id, entity.Code, actor, code is null ? "CATALOG_CREATED" : "CATALOG_UPDATED", before, new { entity.Name, entity.Notes, entity.Status, entity.Version }, token);
        return new WarehouseDto(entity.Code, entity.Name, (await uow.Departments.GetByIdAsync(entity.DepartmentId, token))!.Code, entity.Status, entity.Notes, entity.Version);
    }, ct);
    public Task<LocationDto> SaveLocationAsync(string? code, LocationWrite input, CancellationToken ct) => transaction.ExecuteAsync(async token =>
    {
        var actor = await access.RequireAsync(token, manager: true); Validate(input);
        var warehouse = await warehouses.FirstOrDefaultAsync(w => w.Code == Code(input.WarehouseCode) && w.DepartmentId == actor.DepartmentId, token) ?? throw MaterialErrors.Missing("Warehouse not found.");
        var area = Clean(input.AreaCode) is null ? null : await uow.Areas.FirstOrDefaultAsync(a => a.Code == Code(input.AreaCode), token) ?? throw MaterialErrors.Missing("Area not found.");
        if (input.Status == EntityStatus.Active) { if (warehouse.Status != EntityStatus.Active) throw MaterialErrors.Invalid("Select an active warehouse."); await ActiveAreaAsync(area?.Id, token); }
        MaterialLocation entity; object? before = null;
        if (code is null) { entity = new() { Code = NewCode(input.Code, "VK"), Name = Name(input.Name), WarehouseId = warehouse.Id, AreaId = area?.Id, Status = input.Status }; await locations.AddAsync(entity, token); }
        else
        {
            entity = await locations.FirstOrDefaultTrackedAsync(x => x.Code == Code(code), token) ?? throw MaterialErrors.Missing();
            var originalWarehouse = await warehouses.GetByIdAsync(entity.WarehouseId, token);
            if (originalWarehouse?.DepartmentId != actor.DepartmentId) throw MaterialErrors.Missing();
            Version(entity.Version, input.Version); ImmutableCode(entity.Code, input.Code); before = new { entity.Name, entity.WarehouseId, entity.AreaId, entity.Status, entity.Version };
            if ((entity.WarehouseId != warehouse.Id || entity.AreaId != area?.Id) && await documentLines.ExistsAsync(l => l.LocationId == entity.Id || l.ToLocationId == entity.Id, token)) throw MaterialErrors.Conflict("MATERIAL_LOCATION_IN_USE", "A location referenced by documents cannot move to a different warehouse or area; use a stock transfer.");
            entity.Name = Name(input.Name); entity.WarehouseId = warehouse.Id; entity.AreaId = area?.Id; entity.Status = input.Status; entity.Version++;
        }
        await CatalogAuditAsync("Location", entity.Id, entity.Code, actor, code is null ? "CATALOG_CREATED" : "CATALOG_UPDATED", before, new { entity.Name, entity.WarehouseId, entity.AreaId, entity.Status, entity.Version }, token);
        return new LocationDto(entity.Code, entity.Name, warehouse.Code, warehouse.Name, area?.Code, area?.Name, entity.Status, entity.Version);
    }, ct);
    private async Task ActiveAreaAsync(Guid? id, CancellationToken ct)
    {
        while (id is { } areaId)
        {
            var area = await uow.Areas.GetByIdAsync(areaId, ct) ?? throw MaterialErrors.Missing("Area not found.");
            if (area.Status != EntityStatus.Active) throw MaterialErrors.Invalid("The area or an ancestor is locked.");
            id = area.ParentId;
        }
    }
    private async Task<MaterialLocation> ActiveLocationAsync(Guid id, Guid departmentId, CancellationToken ct)
    {
        var location = await locations.GetByIdAsync(id, ct) ?? throw MaterialErrors.Missing("Location not found.");
        var warehouse = await warehouses.GetByIdAsync(location.WarehouseId, ct) ?? throw MaterialErrors.Missing();
        if (warehouse.DepartmentId != departmentId) throw MaterialErrors.Missing();
        if (location.Status != EntityStatus.Active || warehouse.Status != EntityStatus.Active) throw MaterialErrors.Invalid("The warehouse or location is locked.");
        await ActiveAreaAsync(location.AreaId, ct); return location;
    }
}
