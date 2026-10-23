using CSVC.Core.Common;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Manufacturers;
using CSVC.Core.Suppliers;
using CSVC.Infrastructure;
using CSVC.Infrastructure.Persistence;
using CSVC.UseCases.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CSVC.API.Tests;

public sealed class CatalogRepositoryTests
{
    [Fact]
    public async Task Catalogs_and_their_histories_share_a_commit_and_normalized_lookups_include_locked_records()
    {
        var options = new DbContextOptionsBuilder<CsvcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = "Host=localhost;Database=test;Username=test"
        }).Build());
        services.Replace(ServiceDescriptor.Scoped<CsvcDbContext>(_ => new CsvcDbContext(options)));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var context = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        var category = new DeviceCategory { DisplayCode = " DM01 ", Name = " Phòng\tKỹ   thuật ", Status = EntityStatus.Locked, Notes = "Category note" };
        var supplier = new Supplier { DisplayCode = " NCC01 ", Name = " Phòng\tKỹ   thuật ", Status = EntityStatus.Locked, TaxCode = "0123456789", Address = "Address", Phone = "0900000000", Email = "contact@example.com", Notes = "Supplier note" };
        var manufacturer = new Manufacturer { DisplayCode = " HSX01 ", Name = " Phòng\tKỹ   thuật ", Status = EntityStatus.Locked, Country = "Vietnam", Website = "https://example.com", ShortName = "Brand", ContactInfo = "Contact", Notes = "Manufacturer note" };
        var requestId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        const string changedFields = "[\"name\",\"notes\"]";
        const string afterData = "{\"name\":\"Phòng Kỹ thuật\",\"notes\":\"Note\"}";
        await unit.DeviceCategories.AddAsync(category);
        await unit.Suppliers.AddAsync(supplier);
        await unit.Manufacturers.AddAsync(manufacturer);
        await unit.CategoryHistories.AddAsync(new CategoryHistory
        {
            RequestId = requestId, CategoryId = category.Id, ActionCode = "CATEGORY_CREATED",
            Outcome = AuditOutcome.Success, OccurredAt = occurredAt, ChangedFields = changedFields, AfterData = afterData
        });
        await unit.SupplierHistories.AddAsync(new SupplierHistory
        {
            RequestId = requestId, SupplierId = supplier.Id, ActionCode = "SUPPLIER_CREATED",
            Outcome = AuditOutcome.Success, OccurredAt = occurredAt, ChangedFields = changedFields, AfterData = afterData
        });
        await unit.ManufacturerHistories.AddAsync(new ManufacturerHistory
        {
            RequestId = requestId, ManufacturerId = manufacturer.Id, ActionCode = "MANUFACTURER_CREATED",
            Outcome = AuditOutcome.Success, OccurredAt = occurredAt, ChangedFields = changedFields, AfterData = afterData
        });
        await using var reader = new CsvcDbContext(options);
        Assert.Empty(await reader.DeviceCategories.ToListAsync());
        Assert.Empty(await reader.SupplierHistories.ToListAsync());
        Assert.Equal(6, await unit.CommitAsync());
        context.ChangeTracker.Clear();

        await AssertLookupAsync(category.Id, "DM01", unit.DeviceCategories.GetByDisplayCodeAsync,
            unit.DeviceCategories.DisplayCodeExistsAsync, unit.DeviceCategories.NameExistsAsync);
        await AssertLookupAsync(supplier.Id, "NCC01", unit.Suppliers.GetByDisplayCodeAsync,
            unit.Suppliers.DisplayCodeExistsAsync, unit.Suppliers.NameExistsAsync);
        await AssertLookupAsync(manufacturer.Id, "HSX01", unit.Manufacturers.GetByDisplayCodeAsync,
            unit.Manufacturers.DisplayCodeExistsAsync, unit.Manufacturers.NameExistsAsync);
        Assert.Empty(context.ChangeTracker.Entries());

        var savedCategory = await reader.DeviceCategories.SingleAsync();
        Assert.Equal("DM01", savedCategory.DisplayCode);
        Assert.Equal("Phòng Kỹ thuật", savedCategory.Name);
        Assert.Equal("Category note", savedCategory.Notes);
        var savedSupplier = await reader.Suppliers.SingleAsync();
        Assert.Equal(supplier.TaxCode, savedSupplier.TaxCode);
        Assert.Equal(supplier.Address, savedSupplier.Address);
        Assert.Equal(supplier.Phone, savedSupplier.Phone);
        Assert.Equal(supplier.Email, savedSupplier.Email);
        Assert.Equal(supplier.Notes, savedSupplier.Notes);
        var savedManufacturer = await reader.Manufacturers.SingleAsync();
        Assert.Equal(manufacturer.Country, savedManufacturer.Country);
        Assert.Equal(manufacturer.Website, savedManufacturer.Website);
        Assert.Equal(manufacturer.ShortName, savedManufacturer.ShortName);
        Assert.Equal(manufacturer.ContactInfo, savedManufacturer.ContactInfo);
        Assert.Equal(manufacturer.Notes, savedManufacturer.Notes);
        var categoryHistory = await reader.CategoryHistories.SingleAsync();
        Assert.Equal(category.Id, categoryHistory.CategoryId);
        Assert.Equal(changedFields, categoryHistory.ChangedFields);
        var supplierHistory = await reader.SupplierHistories.SingleAsync();
        Assert.Equal(supplier.Id, supplierHistory.SupplierId);
        Assert.Equal(afterData, supplierHistory.AfterData);
        var manufacturerHistory = await reader.ManufacturerHistories.SingleAsync();
        Assert.Equal(manufacturer.Id, manufacturerHistory.ManufacturerId);
        Assert.Equal(occurredAt, manufacturerHistory.OccurredAt);
        Assert.Equal(AuditOutcome.Success, manufacturerHistory.Outcome);
    }

    private static async Task AssertLookupAsync<T>(Guid id, string displayCode,
        Func<string, CancellationToken, Task<T?>> lookup,
        Func<string, Guid?, CancellationToken, Task<bool>> codeExists,
        Func<string, Guid?, CancellationToken, Task<bool>> nameExists) where T : class
    {
        Assert.NotNull(await lookup(" " + displayCode.ToLowerInvariant() + " ", default));
        Assert.Null(await lookup("MISSING", default));
        Assert.True(await codeExists(" " + displayCode.ToLowerInvariant() + " ", null, default));
        Assert.False(await codeExists(displayCode, id, default));
        Assert.False(await codeExists("MISSING", null, default));
        var name = "  PHÒNG\tKỸ   THUẬT ".Normalize(System.Text.NormalizationForm.FormD);
        Assert.True(await nameExists(name, null, default));
        Assert.False(await nameExists(name, id, default));
        Assert.False(await nameExists("Missing", null, default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup(displayCode, cancelled.Token));
    }
}
