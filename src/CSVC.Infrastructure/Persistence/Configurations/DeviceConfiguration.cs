using CSVC.Core.Accounts;
using CSVC.Core.Areas;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Core.DeviceCategories;
using CSVC.Core.Devices;
using CSVC.Core.Manufacturers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices", table =>
        {
            table.HasCheckConstraint("ck_devices_device_code", "btrim(device_code) <> ''");
            table.HasCheckConstraint("ck_devices_barcode", "btrim(barcode) <> ''");
            table.HasCheckConstraint("ck_devices_name", "btrim(name) <> ''");
            table.HasCheckConstraint("ck_devices_purchase_price", "purchase_price IS NULL OR purchase_price >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(x => x.DeviceCode).HasColumnName("device_code").HasColumnType("varchar(32)").HasMaxLength(32).IsRequired();
        builder.Property<string>("NormalizedDeviceCode").HasColumnName("normalized_device_code")
            .HasComputedColumnSql("upper(btrim(device_code))", stored: true).IsRequired();
        builder.HasIndex("NormalizedDeviceCode").IsUnique().HasDatabaseName("uq_devices_normalized_device_code");
        builder.Property(x => x.Barcode).HasColumnName("barcode").HasColumnType("varchar(32)").HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.Barcode).IsUnique().HasDatabaseName("uq_devices_barcode");
        builder.Property(x => x.Name).HasColumnName("name").HasColumnType("varchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CategoryId).HasColumnName("category_id").HasColumnType("uuid");
        builder.Property(x => x.ManufacturerId).HasColumnName("manufacturer_id").HasColumnType("uuid");
        builder.Property(x => x.Model).HasColumnName("model").HasColumnType("varchar(120)").HasMaxLength(120);
        builder.Property(x => x.SerialNumber).HasColumnName("serial_number").HasColumnType("varchar(100)").HasMaxLength(100);
        builder.Property(x => x.PurchaseDate).HasColumnName("purchase_date").HasColumnType("date");
        builder.Property(x => x.PaidInvoiceCode).HasColumnName("paid_invoice_code").HasColumnType("varchar(64)").HasMaxLength(64);
        builder.Property(x => x.PurchasePrice).HasColumnName("purchase_price").HasColumnType("numeric(18,2)");
        builder.Property(x => x.WarrantyExpiryDate).HasColumnName("warranty_expiry_date").HasColumnType("date");
        builder.Property(x => x.DepartmentId).HasColumnName("department_id").HasColumnType("uuid");
        builder.Property(x => x.AreaId).HasColumnName("area_id").HasColumnType("uuid");
        builder.Property(x => x.ResponsibleAccountId).HasColumnName("responsible_account_id").HasColumnType("uuid");
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("entity_status").HasDefaultValue(EntityStatus.Active);
        builder.Property(x => x.Condition).HasColumnName("condition").HasColumnType("device_condition").HasDefaultValue(DeviceCondition.Good);
        builder.Property(x => x.Notes).HasColumnName("notes").HasColumnType("text");
        builder.Property(x => x.Version).HasColumnName("version").HasColumnType("integer").HasDefaultValue(1).IsConcurrencyToken();
        builder.HasIndex(x => new { x.DepartmentId, x.Status }).HasDatabaseName("ix_devices_department_status");
        builder.HasIndex(x => x.CategoryId).HasDatabaseName("ix_devices_category");
        builder.HasIndex(x => x.ManufacturerId).HasDatabaseName("ix_devices_manufacturer");
        builder.HasIndex(x => x.AreaId).HasDatabaseName("ix_devices_area");
        builder.HasOne<DeviceCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeviceIdentity>().WithMany().HasForeignKey(x => x.DeviceCode).HasPrincipalKey(x => x.DeviceCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeviceIdentity>().WithMany().HasForeignKey(x => x.Barcode).HasPrincipalKey(x => x.Barcode).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Manufacturer>().WithMany().HasForeignKey(x => x.ManufacturerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Area>().WithMany().HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.ResponsibleAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
