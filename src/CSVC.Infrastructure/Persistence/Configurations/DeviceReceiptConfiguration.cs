using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Core.DeviceCategories;
using CSVC.Core.DeviceReceipts;
using CSVC.Core.Devices;
using CSVC.Core.Suppliers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class DeviceReceiptConfiguration : IEntityTypeConfiguration<DeviceReceipt>
{
    public void Configure(EntityTypeBuilder<DeviceReceipt> builder)
    {
        builder.ToTable("device_receipts", table =>
        {
            table.HasCheckConstraint("ck_device_receipts_receipt_code", "btrim(receipt_code) <> ''");
            table.HasCheckConstraint("ck_device_receipts_delivery_batch", "btrim(delivery_batch) <> ''");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(x => x.ReceiptCode).HasColumnName("receipt_code").HasColumnType("varchar(32)").HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.ReceiptCode).IsUnique().HasDatabaseName("uq_device_receipts_code");
        builder.Property(x => x.SupplierId).HasColumnName("supplier_id").HasColumnType("uuid");
        builder.Property(x => x.CarrierName).HasColumnName("carrier_name").HasColumnType("varchar(200)").HasMaxLength(200);
        builder.Property(x => x.RequestCode).HasColumnName("request_code").HasColumnType("varchar(64)").HasMaxLength(64);
        builder.Property(x => x.DeliveryDate).HasColumnName("delivery_date").HasColumnType("date");
        builder.Property(x => x.DeliveryBatch).HasColumnName("delivery_batch").HasColumnType("varchar(100)").HasMaxLength(100);
        builder.Property(x => x.RequestingDepartmentId).HasColumnName("requesting_department_id").HasColumnType("uuid");
        builder.Property(x => x.PaidInvoiceCode).HasColumnName("paid_invoice_code").HasColumnType("varchar(64)").HasMaxLength(64);
        builder.Property(x => x.CreatorAccountId).HasColumnName("creator_account_id").HasColumnType("uuid");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("device_receipt_status");
        builder.Property(x => x.DecisionAccountId).HasColumnName("decision_account_id").HasColumnType("uuid");
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at").HasColumnType("timestamptz");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamptz");
        builder.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasColumnType("varchar(1000)").HasMaxLength(1000);
        builder.Property(x => x.Notes).HasColumnName("notes").HasColumnType("text");
        builder.Property(x => x.Version).HasColumnName("version").HasColumnType("integer").HasDefaultValue(1).IsConcurrencyToken();
        builder.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("ix_device_receipts_status_created");
        builder.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(x => x.RequestingDepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.CreatorAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.DecisionAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeviceReceiptItemConfiguration : IEntityTypeConfiguration<DeviceReceiptItem>
{
    public void Configure(EntityTypeBuilder<DeviceReceiptItem> builder)
    {
        builder.ToTable("device_receipt_items", table =>
        {
            table.HasCheckConstraint("ck_device_receipt_items_name", "btrim(name) <> ''");
            table.HasCheckConstraint("ck_device_receipt_items_device_code", "btrim(device_code) <> ''");
            table.HasCheckConstraint("ck_device_receipt_items_barcode", "btrim(barcode) <> ''");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(x => x.ReceiptId).HasColumnName("receipt_id").HasColumnType("uuid");
        builder.Property(x => x.CategoryId).HasColumnName("category_id").HasColumnType("uuid");
        builder.Property(x => x.Name).HasColumnName("name").HasColumnType("varchar(200)").HasMaxLength(200);
        builder.Property(x => x.DeviceCode).HasColumnName("device_code").HasColumnType("varchar(32)").HasMaxLength(32);
        builder.Property(x => x.Barcode).HasColumnName("barcode").HasColumnType("varchar(32)").HasMaxLength(32);
        builder.Property(x => x.DeviceCodeSource).HasColumnName("device_code_source").HasColumnType("device_code_source");
        builder.Property(x => x.SerialNumber).HasColumnName("serial_number").HasColumnType("varchar(100)").HasMaxLength(100);
        builder.Property(x => x.ParentDeviceCode).HasColumnName("parent_device_code").HasColumnType("varchar(32)").HasMaxLength(32);
        builder.Property(x => x.DeviceId).HasColumnName("device_id").HasColumnType("uuid");
        builder.HasIndex(x => x.DeviceCode).IsUnique().HasDatabaseName("uq_device_receipt_items_device_code");
        builder.HasIndex(x => x.Barcode).IsUnique().HasDatabaseName("uq_device_receipt_items_barcode");
        builder.HasIndex(x => x.DeviceId).IsUnique().HasDatabaseName("uq_device_receipt_items_device_id");
        builder.HasOne<DeviceCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeviceIdentity>().WithMany().HasForeignKey(x => x.DeviceCode).HasPrincipalKey(x => x.DeviceCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeviceIdentity>().WithMany().HasForeignKey(x => x.Barcode).HasPrincipalKey(x => x.Barcode).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
    }
}
