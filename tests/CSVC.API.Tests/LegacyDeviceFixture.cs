using CSVC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CSVC.API.Tests;

// Seed relationships that predate the read-only policy, inside isolated test databases.
// The guard is restored before the transaction commits and before any HTTP operation.
internal static class LegacyDeviceFixture
{
    public static async Task SaveAsync(CsvcDbContext db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE devices DISABLE TRIGGER tr_devices_package_reference_readonly");
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE devices ENABLE TRIGGER tr_devices_package_reference_readonly");
        await transaction.CommitAsync();
    }
}
