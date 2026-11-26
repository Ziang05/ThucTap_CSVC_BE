using CSVC.Core.DeviceCategories;
using CSVC.UseCases.DeviceCategories.DTOs;

namespace CSVC.UseCases.DeviceCategories;

public static class CategoryHierarchy
{
    public static IEnumerable<DeviceCategory> WalkToRoot(
        DeviceCategory category, IReadOnlyDictionary<Guid, DeviceCategory> categories)
    {
        var seen = new HashSet<Guid>();
        for (DeviceCategory? node = category; node is not null && seen.Add(node.Id);)
        {
            yield return node;
            node = node.ParentId is { } parentId ? categories.GetValueOrDefault(parentId) : null;
        }
    }

    public static void Populate(CategoryListItemDto detail, DeviceCategory category,
        IReadOnlyDictionary<Guid, DeviceCategory> categories)
    {
        var path = WalkToRoot(category, categories).ToArray();
        var source = path.FirstOrDefault(node => node.MaintenanceIntervalMonths.HasValue);
        detail.ParentDisplayCode = path.ElementAtOrDefault(1)?.DisplayCode;
        detail.Path = path.Reverse().Select(node => new CategoryPathItemDto(node.DisplayCode, node.Name)).ToArray();
        detail.EffectiveMaintenanceIntervalMonths = source?.MaintenanceIntervalMonths;
        detail.MaintenanceIntervalSourceCode = source?.DisplayCode;
    }
}
