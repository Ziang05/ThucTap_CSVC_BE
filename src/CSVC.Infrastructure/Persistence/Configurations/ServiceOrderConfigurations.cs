using System.Text.RegularExpressions;
using CSVC.Core.Accounts;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

// New workflow enums are stored as checked strings; existing native enum mappings stay unchanged.
internal static class ServiceMapping
{
    public static void Columns<T>(EntityTypeBuilder<T> b) where T : class
    {
        foreach (var p in b.Metadata.GetProperties())
            p.SetColumnName(Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }
    public static void Account<T>(EntityTypeBuilder<T> b, string property) where T : class
        => b.HasOne<Account>().WithMany().HasForeignKey(property).OnDelete(DeleteBehavior.Restrict);
}

public sealed class ServiceOrderConfiguration : IEntityTypeConfiguration<ServiceOrder>
{
    public void Configure(EntityTypeBuilder<ServiceOrder> b)
    {
        b.ToTable("service_orders", t =>
        {
            t.HasCheckConstraint("ck_service_orders_type", "order_type IN ('Maintenance','Repair')");
            t.HasCheckConstraint("ck_service_orders_status", "status IN ('PendingApproval','InProgress','Completed','Canceled')");
            t.HasCheckConstraint("ck_service_orders_cancel", "status <> 'Canceled' OR (canceled_at IS NOT NULL AND canceled_by_account_id IS NOT NULL AND COALESCE(btrim(cancel_reason), '') <> '')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.DisplayCode).HasMaxLength(64).IsRequired();
        b.Property(x => x.CreateRequestHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.OrderType).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.DisplayCode).IsUnique();
        b.HasIndex(x => x.CreateRequestId).IsUnique();
        b.HasIndex(x => x.ApprovalRequestId).IsUnique();
        b.HasIndex(x => x.CancelRequestId).IsUnique();
        b.HasIndex(x => new { x.OrderType, x.Status, x.CreatedAt });
        b.HasIndex(x => new { x.CreatedByAccountId, x.CreatedAt });
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ServiceOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.ExportSlips).WithOne().HasForeignKey(x => x.ServiceOrderId).OnDelete(DeleteBehavior.Restrict);
        ServiceMapping.Account(b, nameof(ServiceOrder.CreatedByAccountId));
        ServiceMapping.Account(b, nameof(ServiceOrder.ApprovedByAccountId));
        ServiceMapping.Account(b, nameof(ServiceOrder.CanceledByAccountId));
        ServiceMapping.Columns(b);
    }
}

public sealed class ServiceOrderItemConfiguration : IEntityTypeConfiguration<ServiceOrderItem>
{
    public void Configure(EntityTypeBuilder<ServiceOrderItem> b)
    {
        b.ToTable("service_order_items", t =>
        {
            t.HasCheckConstraint("ck_service_items_no", "item_no > 0");
            t.HasCheckConstraint("ck_service_items_method", "execution_method IN ('OnSite','External')");
            t.HasCheckConstraint("ck_service_items_status", "execution_status IS NULL OR execution_status IN ('InProgress','NotExecuted','Completed','Failed')");
            t.HasCheckConstraint("ck_service_items_result", "execution_status NOT IN ('Completed','Failed') OR (result_recorded_at IS NOT NULL AND result_recorded_by_account_id IS NOT NULL AND COALESCE(btrim(result_description), '') <> '')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ExecutionMethod).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.ExecutionStatus).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.DeviceCodeSnapshot).HasMaxLength(64);
        b.Property(x => x.DeviceNameSnapshot).HasMaxLength(255);
        b.Property(x => x.SerialSnapshot).HasMaxLength(120);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.ServiceOrderId, x.ItemNo }).IsUnique();
        b.HasIndex(x => new { x.ServiceOrderId, x.DeviceId }).IsUnique();
        b.HasIndex(x => new { x.ServiceOrderId, x.ExecutionStatus });
        b.HasIndex(x => new { x.DeviceId, x.ExecutionStatus });
        b.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.RepairDetail).WithOne().HasForeignKey<RepairItemDetail>(x => x.ServiceOrderItemId).OnDelete(DeleteBehavior.Restrict);
        ServiceMapping.Account(b, nameof(ServiceOrderItem.ResultRecordedByAccountId));
        ServiceMapping.Columns(b);
    }
}

public sealed class RepairItemDetailConfiguration : IEntityTypeConfiguration<RepairItemDetail>
{
    public void Configure(EntityTypeBuilder<RepairItemDetail> b)
    {
        b.ToTable("repair_item_details");
        b.HasKey(x => x.ServiceOrderItemId);
        b.Property(x => x.ServiceOrderItemId).ValueGeneratedNever();
        b.HasIndex(x => x.DamageReportId);
        ServiceMapping.Columns(b);
    }
}

public sealed class ExportSlipConfiguration : IEntityTypeConfiguration<ExportSlip>
{
    public void Configure(EntityTypeBuilder<ExportSlip> b)
    {
        b.ToTable("export_slips", t =>
        {
            t.HasCheckConstraint("ck_export_slips_sequence", "sequence_no > 0");
            t.HasCheckConstraint("ck_export_slips_facility", "btrim(facility_name_snapshot) <> ''");
            t.HasCheckConstraint("ck_export_slips_status", "status IN ('PendingApproval','InProgress','Completed','Canceled')");
            t.HasCheckConstraint("ck_export_slips_result", "(result IS NULL AND status <> 'Completed') OR (result IS NOT NULL AND status = 'Completed' AND completed_at IS NOT NULL AND result_recorded_at IS NOT NULL AND result_recorded_by_account_id IS NOT NULL AND ((result = 'Success' AND COALESCE(btrim(recipient_name), '') <> '') OR (result = 'Failed' AND COALESCE(btrim(failure_reason), '') <> '')))");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.DisplayCode).HasMaxLength(64);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.Result).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.FacilityNameSnapshot).HasMaxLength(255);
        b.Property(x => x.RecipientName).HasMaxLength(255);
        b.Property(x => x.RecipientContact).HasMaxLength(255);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.DisplayCode).IsUnique();
        b.HasIndex(x => x.ResultRequestId).IsUnique();
        b.HasIndex(x => new { x.ServiceOrderId, x.SequenceNo }).IsUnique();
        b.HasIndex(x => new { x.Status, x.CreatedAt });
        b.HasIndex(x => new { x.Result, x.ResultRecordedAt });
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ExportSlipId).OnDelete(DeleteBehavior.Restrict);
        ServiceMapping.Account(b, nameof(ExportSlip.CreatedByAccountId));
        ServiceMapping.Account(b, nameof(ExportSlip.ResultRecordedByAccountId));
        ServiceMapping.Columns(b);
    }
}

public sealed class ExportSlipItemConfiguration : IEntityTypeConfiguration<ExportSlipItem>
{
    public void Configure(EntityTypeBuilder<ExportSlipItem> b)
    {
        b.ToTable("export_slip_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.DeviceCodeSnapshot).HasMaxLength(64);
        b.Property(x => x.DeviceNameSnapshot).HasMaxLength(255);
        b.Property(x => x.SerialSnapshot).HasMaxLength(120);
        b.HasIndex(x => x.ServiceOrderItemId).IsUnique();
        b.HasIndex(x => new { x.ExportSlipId, x.ServiceOrderItemId }).IsUnique();
        b.HasOne<ServiceOrderItem>().WithMany().HasForeignKey(x => x.ServiceOrderItemId).OnDelete(DeleteBehavior.Restrict);
        ServiceMapping.Columns(b);
    }
}

public sealed class ServiceOrderHistoryConfiguration : IEntityTypeConfiguration<ServiceOrderHistory>
{
    public void Configure(EntityTypeBuilder<ServiceOrderHistory> b)
    {
        b.ToTable("service_order_history");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TargetKey).HasMaxLength(80);
        b.Property(x => x.ActionCode).HasMaxLength(64);
        b.Property(x => x.Outcome).HasColumnType("audit_outcome");
        b.Property(x => x.ReasonCode).HasMaxLength(80);
        b.Property(x => x.BeforeData).HasColumnType("jsonb");
        b.Property(x => x.AfterData).HasColumnType("jsonb");
        // Include the order because ORDER is a per-aggregate target key.
        b.HasIndex(x => new { x.RequestId, x.ActionCode, x.ServiceOrderId, x.TargetKey }).IsUnique();
        b.HasIndex(x => new { x.ServiceOrderId, x.OccurredAt });
        b.HasIndex(x => new { x.ServiceOrderItemId, x.OccurredAt });
        b.HasIndex(x => new { x.RelatedExportSlipId, x.OccurredAt });
        b.HasOne<ServiceOrder>().WithMany().HasForeignKey(x => x.ServiceOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ServiceOrderItem>().WithMany().HasForeignKey(x => x.ServiceOrderItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ExportSlip>().WithMany().HasForeignKey(x => x.RelatedExportSlipId).OnDelete(DeleteBehavior.Restrict);
        ServiceMapping.Account(b, nameof(ServiceOrderHistory.ActorAccountId));
        ServiceMapping.Columns(b);
    }
}

public sealed class ExportHistoryConfiguration : IEntityTypeConfiguration<ExportHistory>
{
    public void Configure(EntityTypeBuilder<ExportHistory> b)
    {
        b.ToTable("export_history");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ActionCode).HasMaxLength(64);
        b.Property(x => x.Outcome).HasColumnType("audit_outcome");
        b.Property(x => x.ReasonCode).HasMaxLength(80);
        b.Property(x => x.BeforeData).HasColumnType("jsonb");
        b.Property(x => x.AfterData).HasColumnType("jsonb");
        b.HasIndex(x => new { x.RequestId, x.ActionCode, x.ExportSlipId }).IsUnique();
        b.HasIndex(x => new { x.ExportSlipId, x.OccurredAt });
        b.HasOne<ExportSlip>().WithMany().HasForeignKey(x => x.ExportSlipId).OnDelete(DeleteBehavior.Restrict);
        ServiceMapping.Account(b, nameof(ExportHistory.ActorAccountId));
        ServiceMapping.Columns(b);
    }
}
