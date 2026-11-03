using CSVC.UseCases.Settings;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/settings")]
public sealed class SettingsController(IEquipmentSettingsService settings) : AuthenticatedApiControllerBase
{
    [HttpGet("equipment-department")]
    public async Task<ActionResult<EquipmentSettingsDto>> Get(CancellationToken ct) => Ok(await settings.GetAsync(ct));

    [HttpPut("equipment-department")]
    public async Task<IActionResult> Update(UpdateEquipmentDepartmentRequest request, CancellationToken ct)
    {
        await settings.UpdateAsync(request, ct);
        return NoContent();
    }

    [HttpGet("workflow-context")]
    public async Task<ActionResult<EquipmentWorkflowContextDto>> Context(CancellationToken ct) => Ok(await settings.ContextAsync(ct));
}
