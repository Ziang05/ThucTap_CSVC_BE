using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Core.DeviceTransfers;
using CSVC.Core.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class DeviceTransferConfiguration : IEntityTypeConfiguration<DeviceTransfer>
{
    public void Configure(EntityTypeBuilder<DeviceTransfer> builder)
    {
        builder.ToTable("device_transfers", table =>
        {
            table.HasCheckConstraint("ck_device_transfers_distinct_departments", "source_department_id <> target_department_id");
            table.HasCheckConstraint("ck_device_transfers_reason", "btrim(reason) <> ''");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(x => x.TransferCode).HasColumnName("transfer_code").HasColumnType("varchar(32)").HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.TransferCode).IsUnique().HasDatabaseName("uq_device_transfers_code");
        builder.Property(x => x.SourceDepartmentId).HasColumnName("source_department_id").HasColumnType("uuid");
        builder.Property(x => x.TargetDepartmentId).HasColumnName("target_department_id").HasColumnType("uuid");
        builder.Property(x => x.TargetResponsibleAccountId).HasColumnName("target_responsible_account_id").HasColumnType("uuid");
        builder.Property(x => x.Reason).HasColumnName("reason").HasColumnType("varchar(1000)").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.CreatorAccountId).HasColumnName("creator_account_id").HasColumnType("uuid");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("device_transfer_status");
        builder.Property(x => x.DecisionAccountId).HasColumnName("decision_account_id").HasColumnType("uuid");
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at").HasColumnType("timestamptz");
        builder.Property(x => x.SourceHandoverAccountId).HasColumnName("source_handover_account_id").HasColumnType("uuid");
        builder.Property(x => x.TargetHandoverAccountId).HasColumnName("target_handover_account_id").HasColumnType("uuid");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamptz");
        builder.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasColumnType("varchar(1000)").HasMaxLength(1000);
        builder.Property(x => x.Notes).HasColumnName("notes").HasColumnType("text");
        builder.Property(x => x.Version).HasColumnName("version").HasColumnType("integer").HasDefaultValue(1).IsConcurrencyToken();
        builder.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("ix_device_transfers_status_created");
        builder.HasOne<Department>().WithMany().HasForeignKey(x => x.SourceDepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(x => x.TargetDepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.TargetResponsibleAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.CreatorAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.DecisionAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.SourceHandoverAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.TargetHandoverAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeviceTransferItemConfiguration : IEntityTypeConfiguration<DeviceTransferItem>
{
    public void Configure(EntityTypeBuilder<DeviceTransferItem> builder)
    {
        builder.ToTable("device_transfer_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(x => x.TransferId).HasColumnName("transfer_id").HasColumnType("uuid");
        builder.Property(x => x.DeviceId).HasColumnName("device_id").HasColumnType("uuid");
        builder.Property(x => x.ConditionAtHandover).HasColumnName("condition_at_handover").HasColumnType("device_condition");
        builder.Property(x => x.HandoverNote).HasColumnName("handover_note").HasColumnType("varchar(1000)").HasMaxLength(1000);
        builder.HasIndex(x => new { x.TransferId, x.DeviceId }).IsUnique().HasDatabaseName("uq_device_transfer_items_transfer_device");
        builder.HasIndex(x => x.DeviceId).HasDatabaseName("ix_device_transfer_items_device");
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
    }
}
