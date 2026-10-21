using CSVC.UseCases.Areas.Models;
using Microsoft.EntityFrameworkCore;

namespace CSVC.Infrastructure.Persistence.Repositories;

internal static class AreaTreeQueries
{
    // The path arrays are also a visited set: malformed legacy data cannot recurse forever.
    public static IQueryable<AreaReadModel> ReadTree(CsvcDbContext context)
        => context.Database.SqlQueryRaw<AreaReadModel>("""
            WITH RECURSIVE tree AS (
                SELECT a.id, a.code, a.parent_id, a.name, a.normalized_name, a.type_label, a.description,
                       a.status = 'LOCKED' AS is_locked, a.status = 'LOCKED' AS effectively_locked,
                       0 AS depth, ARRAY[a.id] AS path_ids, ARRAY[a.name::text] AS path_names
                FROM areas a WHERE a.parent_id IS NULL
                UNION ALL
                SELECT a.id, a.code, a.parent_id, a.name, a.normalized_name, a.type_label, a.description,
                       a.status = 'LOCKED', t.effectively_locked OR a.status = 'LOCKED',
                       t.depth + 1, t.path_ids || a.id, t.path_names || a.name::text
                FROM areas a JOIN tree t ON a.parent_id = t.id
                WHERE NOT a.id = ANY(t.path_ids)
            )
            SELECT t.id AS "Id", t.code AS "Code", t.parent_id AS "ParentId", t.name AS "Name",
                   t.normalized_name AS "NormalizedName", t.type_label AS "TypeLabel", t.description AS "Description",
                   t.is_locked AS "IsLocked", t.effectively_locked AS "IsEffectivelyLocked", t.depth AS "Depth",
                   t.path_ids AS "PathIds", t.path_names AS "PathNames",
                   (SELECT count(*)::integer FROM areas c WHERE c.parent_id = t.id) AS "ChildCount"
            FROM tree t
            """);

    // Includes the selected node. UNION prevents a loop even if data was imported incorrectly.
    public static IQueryable<Guid> SubtreeIds(CsvcDbContext context, Guid id)
        => context.Database.SqlQuery<Guid>($"""
            WITH RECURSIVE branch AS (
                SELECT id FROM areas WHERE id = {id}
                UNION
                SELECT a.id FROM areas a JOIN branch b ON a.parent_id = b.id
            )
            SELECT id AS "Value" FROM branch
            """);
}
