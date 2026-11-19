using CSVC.UseCases.Materials;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/materials")]
public sealed class MaterialsController(IMaterialService service) : AuthenticatedApiControllerBase
{
    [HttpGet("units")] public async Task<IActionResult> Units([FromQuery] MaterialQuery q, CancellationToken ct) => Ok(await service.UnitsAsync(q, ct));
    [HttpPost("units")] public async Task<IActionResult> Unit(UnitWrite input, CancellationToken ct) => Ok(await service.SaveUnitAsync(null, input, ct));
    [HttpPut("units/{code}")] public async Task<IActionResult> Unit(string code, UnitWrite input, CancellationToken ct) => Ok(await service.SaveUnitAsync(code, input, ct));
    [HttpGet] public async Task<IActionResult> Items([FromQuery] MaterialQuery q, CancellationToken ct) => Ok(await service.MaterialsAsync(q, ct));
    [HttpPost] public async Task<IActionResult> Item(MaterialWrite input, CancellationToken ct) => Ok(await service.SaveMaterialAsync(null, input, ct));
    [HttpPut("{code}")] public async Task<IActionResult> Item(string code, MaterialWrite input, CancellationToken ct) => Ok(await service.SaveMaterialAsync(code, input, ct));
    [HttpGet("warehouses")] public async Task<IActionResult> Warehouses([FromQuery] MaterialQuery q, CancellationToken ct) => Ok(await service.WarehousesAsync(q, ct));
    [HttpPost("warehouses")] public async Task<IActionResult> Warehouse(WarehouseWrite input, CancellationToken ct) => Ok(await service.SaveWarehouseAsync(null, input, ct));
    [HttpPut("warehouses/{code}")] public async Task<IActionResult> Warehouse(string code, WarehouseWrite input, CancellationToken ct) => Ok(await service.SaveWarehouseAsync(code, input, ct));
    [HttpGet("locations")] public async Task<IActionResult> Locations([FromQuery] MaterialQuery q, CancellationToken ct) => Ok(await service.LocationsAsync(q, ct));
    [HttpPost("locations")] public async Task<IActionResult> Location(LocationWrite input, CancellationToken ct) => Ok(await service.SaveLocationAsync(null, input, ct));
    [HttpPut("locations/{code}")] public async Task<IActionResult> Location(string code, LocationWrite input, CancellationToken ct) => Ok(await service.SaveLocationAsync(code, input, ct));
    [HttpGet("balances")] public async Task<IActionResult> Balances([FromQuery] MaterialQuery q, CancellationToken ct) => Ok(await service.BalancesAsync(q, ct));
    [HttpGet("report")] public async Task<IActionResult> Report([FromQuery] MaterialQuery q, CancellationToken ct) => Ok(await service.ReportAsync(q, ct));
    [HttpGet("movements")] public async Task<IActionResult> Movements([FromQuery] MaterialQuery q, CancellationToken ct) => Ok(await service.MovementsAsync(q, ct));
    [HttpGet("documents")] public async Task<IActionResult> Documents([FromQuery] MaterialQuery q, CancellationToken ct) => Ok(await service.DocumentsAsync(q, ct));
    [HttpGet("documents/{code}")] public async Task<IActionResult> Document(string code, CancellationToken ct) => Ok(await service.GetDocumentAsync(code, ct));
    [HttpPost("documents")] public async Task<IActionResult> Create(CreateMaterialDocument input, CancellationToken ct) => Ok(await service.CreateDocumentAsync(input, ct));
    [HttpPut("documents/{code}")] public async Task<IActionResult> Update(string code, UpdateMaterialDocument input, CancellationToken ct) { await service.UpdateDocumentAsync(code, input, ct); return NoContent(); }
    [HttpPost("documents/{code}/{operation}")] public async Task<IActionResult> Act(string code, string operation, MaterialAction input, CancellationToken ct) { await service.ActAsync(code, operation, input, ct); return NoContent(); }
    [HttpGet("documents/{code}/history")] public async Task<IActionResult> History(string code, CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) => Ok(await service.HistoryAsync(code, page, pageSize, ct));
    [HttpGet("changes")] public async Task<IActionResult> Changes([FromQuery] string? deviceCode, [FromQuery] MaterialQuery q, CancellationToken ct) => Ok(await service.ChangesAsync(deviceCode, q, ct));
    [HttpPost("changes")] public async Task<IActionResult> Change(RecordEquipmentChange input, CancellationToken ct) => Ok(await service.RecordChangeAsync(input, ct));
    [HttpGet("uses")] public async Task<IActionResult> Uses([FromQuery] MaterialQuery q, CancellationToken ct) => Ok(await service.WorkUsesAsync(q, ct));
    [HttpPost("uses")] public async Task<IActionResult> Use(RecordMaterialWorkUse input, CancellationToken ct) => Ok(await service.RecordWorkUseAsync(input, ct));
}
