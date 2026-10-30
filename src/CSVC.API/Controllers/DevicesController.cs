using CSVC.API.Controllers;
using CSVC.UseCases.Common;
using CSVC.UseCases.Devices;
using CSVC.UseCases.Devices.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/devices")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class DevicesController(IDeviceService devices) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<DeviceDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DeviceDto>>> List([FromQuery] DeviceListQuery query, CancellationToken cancellationToken)
        => Ok(await devices.ListAsync(query, cancellationToken));

    [HttpGet("{deviceCode}/children")]
    [ProducesResponseType<PagedResult<DeviceDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DeviceDto>>> Children(string deviceCode, [FromQuery] DeviceListQuery query, CancellationToken cancellationToken)
    {
        query.ParentDeviceCode = deviceCode;
        return Ok(await devices.ListAsync(query, cancellationToken));
    }

    [HttpGet("by-barcode/{barcode}")]
    [ProducesResponseType<DeviceDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DeviceDto>> GetByBarcode(string barcode, CancellationToken cancellationToken)
        => Ok(await devices.GetByBarcodeAsync(barcode, cancellationToken));

    [HttpGet("{deviceCode}")]
    [ProducesResponseType<DeviceDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DeviceDto>> Get(string deviceCode, CancellationToken cancellationToken)
        => Ok(await devices.GetAsync(deviceCode, cancellationToken));

    [HttpPost]
    [ProducesResponseType<DeviceDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<DeviceDto>> Create(CreateDeviceRequest request, CancellationToken cancellationToken)
    {
        var device = await devices.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { deviceCode = device.DeviceCode }, device);
    }

    [HttpPut("{deviceCode}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(string deviceCode, UpdateDeviceRequest request, CancellationToken cancellationToken)
    {
        await devices.UpdateAsync(deviceCode, request, cancellationToken);
        return NoContent();
    }

    [HttpPut("{deviceCode}/parent")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetParent(string deviceCode, DeviceParentUpdateRequest request, CancellationToken cancellationToken)
    {
        await devices.SetParentAsync(deviceCode, request.ParentDeviceCode, cancellationToken);
        return NoContent();
    }

    [HttpPut("{deviceCode}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Lock(string deviceCode, CancellationToken cancellationToken)
    {
        await devices.SetLockedAsync(deviceCode, true, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{deviceCode}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unlock(string deviceCode, CancellationToken cancellationToken)
    {
        await devices.SetLockedAsync(deviceCode, false, cancellationToken);
        return NoContent();
    }

    [HttpGet("{deviceCode}/history")]
    [ProducesResponseType<PagedResult<DeviceHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DeviceHistoryDto>>> History(string deviceCode, [FromQuery] DeviceHistoryQuery query, CancellationToken cancellationToken)
        => Ok(await devices.HistoryAsync(deviceCode, query, cancellationToken));
}
