using CSVC.Core.Common;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Manufacturers;
using CSVC.Core.Suppliers;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSVC.API.Tests;

internal static class CatalogPostgreSqlChecks
{
    public static async Task VerifyAsync(string connectionString, IUnitOfWork unit, CsvcDbContext context, Guid actorId)
    {
        var category = new DeviceCategory { DisplayCode = " DM01 ", Name = " Phòng\tKỹ   thuật ", Status = EntityStatus.Locked };
        var supplier = new Supplier { DisplayCode = " NCC01 ", Name = " Phòng\tKỹ   thuật ", Status = EntityStatus.Locked, TaxCode = "0123456789" };
        var manufacturer = new Manufacturer { DisplayCode = " HSX01 ", Name = " Phòng\tKỹ   thuật ", Status = EntityStatus.Locked, Website = "https://example.com" };
        var requestId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        const string changedFields = "[\"name\"]";
        const string afterData = "{\"name\":\"Phòng Kỹ thuật\"}";
        await unit.DeviceCategories.AddAsync(category);
        await unit.Suppliers.AddAsync(supplier);
        await unit.Manufacturers.AddAsync(manufacturer);
        await unit.CategoryHistories.AddAsync(new CategoryHistory
        {
            RequestId = requestId, ActorAccountId = actorId, CategoryId = category.Id, ActionCode = "CATEGORY_CREATED",
            Outcome = AuditOutcome.Success, OccurredAt = now, ChangedFields = changedFields, AfterData = afterData
        });
        await unit.SupplierHistories.AddAsync(new SupplierHistory
        {
            RequestId = requestId, ActorAccountId = actorId, SupplierId = supplier.Id, ActionCode = "SUPPLIER_CREATED",
            Outcome = AuditOutcome.Success, OccurredAt = now, ChangedFields = changedFields, AfterData = afterData
        });
        await unit.ManufacturerHistories.AddAsync(new ManufacturerHistory
        {
            RequestId = requestId, ActorAccountId = actorId, ManufacturerId = manufacturer.Id, ActionCode = "MANUFACTURER_CREATED",
            Outcome = AuditOutcome.Success, OccurredAt = now, ChangedFields = changedFields, AfterData = afterData
        });
        Assert.Equal(6, await unit.CommitAsync());
        context.ChangeTracker.Clear();
        Assert.Equal(category.Id, (await unit.DeviceCategories.GetByDisplayCodeAsync(" dm01 "))?.Id);
        Assert.Equal(supplier.Id, (await unit.Suppliers.GetByDisplayCodeAsync(" ncc01 "))?.Id);
        Assert.Equal(manufacturer.Id, (await unit.Manufacturers.GetByDisplayCodeAsync(" hsx01 "))?.Id);
        Assert.True(await unit.DeviceCategories.NameExistsAsync("PHÒNG KỸ THUẬT", null, default));
        Assert.True(await unit.Suppliers.NameExistsAsync("PHÒNG KỸ THUẬT", null, default));
        Assert.True(await unit.Manufacturers.NameExistsAsync("PHÒNG KỸ THUẬT", null, default));
        var categoryHistory = Assert.Single(await unit.CategoryHistories.GetAllAsync());
        Assert.Equal(category.Id, categoryHistory.CategoryId);
        Assert.Equal(actorId, categoryHistory.ActorAccountId);
        Assert.Equal(AuditOutcome.Success, categoryHistory.Outcome);
        Assert.Contains("name", categoryHistory.ChangedFields!);
        Assert.Equal(supplier.Id, Assert.Single(await unit.SupplierHistories.GetAllAsync()).SupplierId);
        Assert.Equal(manufacturer.Id, Assert.Single(await unit.ManufacturerHistories.GetAllAsync()).ManufacturerId);
        Assert.Empty(context.ChangeTracker.Entries());

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        (string Table, string History, string Target, string Code)[] catalogs =
        [
            ("device_categories", "category_history", "category_id", "DM01"),
            ("suppliers", "supplier_history", "supplier_id", "NCC01"),
            ("manufacturers", "manufacturer_history", "manufacturer_id", "HSX01")
        ];
        foreach (var catalog in catalogs)
        {
            await RejectAsync(connection,
                $"INSERT INTO {catalog.Table}(id,display_code,name,status) VALUES (@id,@code,'Different name','LOCKED')",
                PostgresErrorCodes.UniqueViolation, $"uq_{catalog.Table}_normalized_display_code",
                ("id", Guid.NewGuid()), ("code", " " + catalog.Code.ToLowerInvariant() + " "));
            await RejectAsync(connection,
                $"INSERT INTO {catalog.Table}(id,display_code,name,status) VALUES (@id,'OTHER',@name,'LOCKED')",
                PostgresErrorCodes.UniqueViolation, $"uq_{catalog.Table}_normalized_name",
                ("id", Guid.NewGuid()), ("name", "  PHÒNG\tKỸ   THUẬT ".Normalize(System.Text.NormalizationForm.FormD)));
            await RejectAsync(connection,
                $"INSERT INTO {catalog.Table}(id,display_code,name) VALUES (@id,'EMPTY',E' \\t ')",
                PostgresErrorCodes.CheckViolation, $"ck_{catalog.Table}_name", ("id", Guid.NewGuid()));
            await RejectAsync(connection,
                $"INSERT INTO {catalog.Table}(id,display_code,name) VALUES (@id,'  ','Empty code')",
                PostgresErrorCodes.CheckViolation, $"ck_{catalog.Table}_display_code", ("id", Guid.NewGuid()));
            await RejectAsync(connection,
                $"INSERT INTO {catalog.History}(id,request_id,action_code,outcome,occurred_at) SELECT @id,request_id,action_code,outcome,occurred_at FROM {catalog.History} LIMIT 1",
                PostgresErrorCodes.UniqueViolation, $"uq_{catalog.History}_request_action", ("id", Guid.NewGuid()));
            await RejectAsync(connection,
                $"INSERT INTO {catalog.History}(id,request_id,action_code,outcome,occurred_at,{catalog.Target}) VALUES (@id,@request,'FAILED_CREATE','FAILED',now(),@missing)",
                PostgresErrorCodes.ForeignKeyViolation, null,
                ("id", Guid.NewGuid()), ("request", Guid.NewGuid()), ("missing", Guid.NewGuid()));
        }

        var trackedCategory = await unit.DeviceCategories.GetByIdAsync(category.Id);
        Assert.NotNull(trackedCategory);
        trackedCategory.Name = "Must roll back";
        await unit.CategoryHistories.AddAsync(new CategoryHistory
        {
            RequestId = requestId, CategoryId = category.Id, ActionCode = "CATEGORY_CREATED",
            Outcome = AuditOutcome.Success, OccurredAt = now
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => unit.CommitAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        context.ChangeTracker.Clear();
        Assert.Equal("Phòng Kỹ thuật", (await unit.DeviceCategories.GetByDisplayCodeAsync("DM01"))?.Name);
        Assert.Single(await unit.CategoryHistories.GetAllAsync());
    }

    private static async Task RejectAsync(NpgsqlConnection connection, string sql, string sqlState, string? constraint,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        var error = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
        Assert.Equal(sqlState, error.SqlState);
        if (constraint is not null) Assert.Equal(constraint, error.ConstraintName);
    }
}
