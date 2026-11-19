using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CSVC.API.Tests;

public sealed class AssignmentRemovalTests
{
    [PostgreSqlFact]
    public async Task Direct_intervention_needs_no_task_and_survives_downgrade_and_upgrade()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        using var response = await f.Staff.PostAsJsonAsync("/api/v1/materials/changes", new
        {
            requestId = Guid.NewGuid(), deviceCode = "TB-ROLE-001", kind = "Inspect", target = "Direct inspection",
            beforeValue = "Unchecked", afterValue = "Checked", reason = "Record without assignment"
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await f.InDb(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261112180959_MaterialInventory");
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM equipment_changes c JOIN work_tasks t ON t.id=c.work_task_id WHERE c.target='Direct inspection'").SingleAsync());
            await migrator.MigrateAsync();
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM equipment_changes WHERE target='Direct inspection'").SingleAsync());
        });
    }
    [PostgreSqlFact]
    public async Task Assignment_endpoints_are_removed_and_previous_records_survive_schema_transition()
    {
        await using var f = await EquipmentStaffWorkflowTests.Fixture.CreateAsync();
        using var route = await f.Staff.GetAsync("/api/v1/work-tasks");
        Assert.Equal(HttpStatusCode.NotFound, route.StatusCode);
        await f.InDb(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261112180959_MaterialInventory");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO work_tasks(id,display_code,title,kind,source,source_code,department_id,
                  status,created_by_account_id,created_at,automatic,version)
                SELECT '11111111-1111-1111-1111-111111111111','CV-ARCHIVE','Previous inventory assignment',
                  'Inventory','Manual','MAN-ARCHIVE',p.department_id,'Unassigned',a.id,now(),false,1
                  FROM accounts a JOIN user_profiles p ON p.id=a.user_profile_id WHERE a.account_code='TK_STAFF';
                """);
            await migrator.MigrateAsync();
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM retired_assignment_context WHERE kind='task' AND id='11111111-1111-1111-1111-111111111111'").SingleAsync());
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema='public' AND table_name='work_tasks'").SingleAsync());
            await migrator.MigrateAsync("20261112180959_MaterialInventory");
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM work_tasks WHERE display_code='CV-ARCHIVE'").SingleAsync());
            await migrator.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }
}
