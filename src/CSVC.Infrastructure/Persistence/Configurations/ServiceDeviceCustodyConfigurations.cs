using CSVC.Core.Accounts;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class ServiceOrderDeviceHoldConfiguration : IEntityTypeConfiguration<ServiceOrderDeviceHold>
{
    public void Configure(EntityTypeBuilder<ServiceOrderDeviceHold> b)
    {
        b.ToTable("service_order_device_holds", t => t.HasCheckConstraint("ck_service_hold_version", "version > 0"));
        b.HasKey(x => x.DeviceId); b.Property(x => x.DeviceId).ValueGeneratedNever();
        b.HasIndex(x => x.ServiceOrderItemId).IsUnique(); b.HasIndex(x => new { x.ServiceOrderId, x.HeldAt });
        b.Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken();
        b.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ServiceOrder>().WithMany().HasForeignKey(x => x.ServiceOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ServiceOrderItem>().WithMany().HasForeignKey(x => new { x.ServiceOrderItemId, x.ServiceOrderId, x.DeviceId })
            .HasPrincipalKey(x => new { x.Id, x.ServiceOrderId, x.DeviceId }).OnDelete(DeleteBehavior.Restrict);
        ServiceMapping.Account(b, nameof(ServiceOrderDeviceHold.HeldByAccountId)); ServiceMapping.Columns(b);
    }
}
public sealed class ExportDeviceGuardConfiguration : IEntityTypeConfiguration<ExportDeviceGuard>
{
    public void Configure(EntityTypeBuilder<ExportDeviceGuard> b)
    {
        b.ToTable("export_device_guards", t => t.HasCheckConstraint("ck_export_guard_state", "guard_state IN ('HeldForExport','AwaitingReturn') AND version > 0 AND changed_at >= acquired_at"));
        b.HasKey(x => x.DeviceId); b.Property(x => x.DeviceId).ValueGeneratedNever();
        b.HasIndex(x => x.ExportSlipItemId).IsUnique(); b.HasIndex(x => new { x.GuardState, x.ChangedAt });
        b.Property(x => x.GuardState).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken();
        b.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ExportSlipItem>().WithMany().HasForeignKey(x => x.ExportSlipItemId).OnDelete(DeleteBehavior.Restrict);
        ServiceMapping.Columns(b);
    }
}
public sealed class ExportReturnSlipConfiguration : IEntityTypeConfiguration<ExportReturnSlip>
{
    public void Configure(EntityTypeBuilder<ExportReturnSlip> b)
    {
        b.ToTable("export_return_slips", t =>
        {
            t.HasCheckConstraint("ck_export_return_header", "status = 'Completed' AND sequence_no > 0 AND btrim(display_code) <> '' AND btrim(facility_name_snapshot) <> '' AND btrim(handover_person_name) <> '' AND received_at <= created_at AND version > 0");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.Id, x.ExportSlipId }).HasName("uq_export_return_slips_source_id");
        b.Property(x => x.DisplayCode).HasMaxLength(64); b.Property(x => x.Status).HasMaxLength(24).HasDefaultValue("Completed");
        b.Property(x => x.FacilityNameSnapshot).HasMaxLength(255); b.Property(x => x.HandoverPersonName).HasMaxLength(255);
        b.Property(x => x.HandoverPersonContact).HasMaxLength(255); b.Property(x => x.CreateRequestHash).HasMaxLength(64);
        b.Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken();
        b.HasIndex(x => x.DisplayCode).IsUnique(); b.HasIndex(x => x.CreateRequestId).IsUnique();
        b.HasIndex(x => new { x.ExportSlipId, x.SequenceNo }).IsUnique();
        b.HasIndex(x => new { x.ExportSlipId, x.ReceivedAt }); b.HasIndex(x => new { x.CreatedByAccountId, x.CreatedAt });
        b.HasIndex(x => new { x.ReceivedByAccountId, x.ReceivedAt });
        b.HasOne<ExportSlip>().WithMany().HasForeignKey(x => x.ExportSlipId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Items).WithOne(x => x.ReturnSlip).HasForeignKey(x => new { x.ReturnSlipId, x.ExportSlipId })
            .HasPrincipalKey(x => new { x.Id, x.ExportSlipId }).OnDelete(DeleteBehavior.Restrict);
        ServiceMapping.Account(b, nameof(ExportReturnSlip.CreatedByAccountId)); ServiceMapping.Account(b, nameof(ExportReturnSlip.ReceivedByAccountId));
        ServiceMapping.Columns(b);
    }
}
public sealed class ExportReturnSlipItemConfiguration : IEntityTypeConfiguration<ExportReturnSlipItem>
{
    public void Configure(EntityTypeBuilder<ExportReturnSlipItem> b)
    {
        b.ToTable("export_return_slip_items", t => t.HasCheckConstraint("ck_export_return_condition", "condition_at_return IN ('GOOD','DAMAGED')"));
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.DeviceCodeSnapshot).HasMaxLength(64); b.Property(x => x.DeviceNameSnapshot).HasMaxLength(255);
        b.Property(x => x.SerialSnapshot).HasMaxLength(120); b.Property(x => x.ConditionAtReturn).HasColumnType("device_condition");
        b.HasIndex(x => x.ExportSlipItemId).IsUnique(); b.HasIndex(x => new { x.ReturnSlipId, x.ExportSlipItemId }).IsUnique();
        b.HasIndex(x => new { x.ExportSlipId, x.ReturnSlipId }); b.HasIndex(x => x.ConditionAtReturn);
        b.HasOne<ExportSlipItem>().WithOne(x => x.ReturnItem).HasForeignKey<ExportReturnSlipItem>(x => new { x.ExportSlipItemId, x.ExportSlipId })
            .HasPrincipalKey<ExportSlipItem>(x => new { x.Id, x.ExportSlipId }).OnDelete(DeleteBehavior.Restrict);
        ServiceMapping.Columns(b);
    }
}
public sealed class ExportReturnHistoryConfiguration : IEntityTypeConfiguration<ExportReturnHistory>
{
    public void Configure(EntityTypeBuilder<ExportReturnHistory> b)
    {
        b.ToTable("export_return_history"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TargetKey).HasMaxLength(80); b.Property(x => x.RequestPayloadHash).HasMaxLength(64);
        b.Property(x => x.ActionCode).HasMaxLength(64); b.Property(x => x.Outcome).HasColumnType("audit_outcome");
        b.Property(x => x.BeforeData).HasColumnType("jsonb"); b.Property(x => x.AfterData).HasColumnType("jsonb");
        b.HasIndex(x => new { x.RequestId, x.ActionCode, x.ReturnSlipId, x.TargetKey }).IsUnique();
        b.HasIndex(x => new { x.ReturnSlipId, x.OccurredAt }); b.HasIndex(x => new { x.ReturnSlipItemId, x.OccurredAt });
        b.HasIndex(x => new { x.ActorAccountId, x.OccurredAt });
        b.HasOne<ExportReturnSlip>().WithMany().HasForeignKey(x => x.ReturnSlipId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ExportReturnSlipItem>().WithMany().HasForeignKey(x => x.ReturnSlipItemId).OnDelete(DeleteBehavior.Restrict);
        ServiceMapping.Account(b, nameof(ExportReturnHistory.ActorAccountId)); ServiceMapping.Columns(b);
    }
}
