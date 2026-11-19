using CSVC.UseCases.Common;

namespace CSVC.UseCases.Inventories;

public sealed record InventoryDeviceOption(Guid Id, string Code, string Name, string? Serial);
public sealed record InventoryAssigneeOption(Guid Id, string Code, string Name);

public sealed partial class InventoryService
{
    public async Task<PagedResult<InventoryDeviceOption>> DeviceOptionsAsync(Guid departmentId, string? search, int page, int size, CancellationToken ct)
    {
        Equipment(await ActorAsync(ct));
        Page(page, size);
        if (search?.Length > 200) throw Invalid("INVALID_FILTER", "Search is limited to 200 characters.");
        await DepartmentAsync(departmentId, ct);
        return await repo.DeviceOptionsAsync(departmentId, search?.Trim().ToLowerInvariant(), page, size, ct);
    }

    public async Task<PagedResult<InventoryAssigneeOption>> AssigneeOptionsAsync(string? search, int page, int size, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        Equipment(actor);
        Page(page, size);
        if (search?.Length > 200) throw Invalid("INVALID_FILTER", "Search is limited to 200 characters.");
        return await repo.AssigneeOptionsAsync(actor.DepartmentId, search?.Trim().ToLowerInvariant(), page, size, ct);
    }
}
