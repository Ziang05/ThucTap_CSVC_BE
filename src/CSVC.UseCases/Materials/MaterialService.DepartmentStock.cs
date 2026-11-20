using CSVC.Core.Common;
using CSVC.Core.Materials;

namespace CSVC.UseCases.Materials;

public sealed partial class MaterialService
{
    // Retain the existing stock/ledger schema. The current workflow uses one internal
    // stock account for QTTB and never requires warehouse/location setup from users.
    private async Task<MaterialLocation> DepartmentStockAsync(MaterialActor actor, CancellationToken ct)
    {
        var suffix = actor.DepartmentId.ToString("N")[..30].ToUpperInvariant();
        var warehouseCode = "KQ" + suffix; var locationCode = "VQ" + suffix;
        var warehouse = await warehouses.FirstOrDefaultAsync(w => w.Code == warehouseCode, ct);
        if (warehouse is null)
        {
            warehouse = new MaterialWarehouse { Code = warehouseCode, Name = "Phòng QTTB", DepartmentId = actor.DepartmentId, Status = EntityStatus.Active };
            await warehouses.AddAsync(warehouse, ct);
            await CatalogAuditAsync("Warehouse", warehouse.Id, warehouse.Code, actor, "DEPARTMENT_STOCK_INITIALIZED", null,
                new { warehouse.Name, warehouse.DepartmentId }, ct);
        }
        else if (warehouse.DepartmentId != actor.DepartmentId || warehouse.Status != EntityStatus.Active)
            throw MaterialErrors.Invalid("The QTTB stock account is unavailable.");
        var location = await locations.FirstOrDefaultAsync(l => l.Code == locationCode, ct);
        if (location is null)
        {
            location = new MaterialLocation { Code = locationCode, Name = "Phòng QTTB", WarehouseId = warehouse.Id, Status = EntityStatus.Active };
            await locations.AddAsync(location, ct);
            await CatalogAuditAsync("Location", location.Id, location.Code, actor, "DEPARTMENT_STOCK_INITIALIZED", null,
                new { location.Name, location.WarehouseId }, ct);
        }
        else if (location.WarehouseId != warehouse.Id || location.Status != EntityStatus.Active)
            throw MaterialErrors.Invalid("The QTTB stock account is unavailable.");
        return location;
    }
}
