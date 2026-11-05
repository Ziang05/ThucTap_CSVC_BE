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
        builder.Property(x => x.InitiationType).HasColumnName("initiation_type").HasConversion<string>().HasMaxLength(32).HasDefaultValue(TransferInitiationType.SourceDepartment);
        builder.Property(x => x.CreateRequestId).HasColumnName("create_request_id");
        builder.Property(x => x.CreateRequestHash).HasColumnName("create_request_hash").HasMaxLength(64);
        builder.HasIndex(x => new { x.CreatorAccountId, x.CreateRequestId }).IsUnique().HasFilter("create_request_id IS NOT NULL");
        builder.Property(x => x.SourceAcceptedByAccountId).HasColumnName("source_accepted_by_account_id");
        builder.Property(x => x.SourceAcceptedAt).HasColumnName("source_accepted_at");
        builder.Property(x => x.SourceRejectedByAccountId).HasColumnName("source_rejected_by_account_id");
        builder.Property(x => x.SourceRejectedAt).HasColumnName("source_rejected_at");
        builder.Property(x => x.SourceRejectionReason).HasColumnName("source_rejection_reason").HasMaxLength(1000);
        builder.Property(x => x.SourceHandoverConfirmedByAccountId).HasColumnName("source_handover_confirmed_by_account_id");
        builder.Property(x => x.SourceHandoverConfirmedAt).HasColumnName("source_handover_confirmed_at");
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.SourceAcceptedByAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.SourceRejectedByAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.SourceHandoverConfirmedByAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("ck_equipment_recovery_workflow", """
            initiation_type IN ('SourceDepartment','EquipmentDepartment') AND
            (initiation_type = 'SourceDepartment' OR (
                transfer_type = 'Recovery' AND create_request_id IS NOT NULL AND create_request_id <> '00000000-0000-0000-0000-000000000000'::uuid
                AND create_request_hash IS NOT NULL AND create_request_hash ~ '^[0-9A-F]{64}$'
                AND receiver_confirmed_account_id IS NULL AND receiver_confirmed_at IS NULL
                AND receiver_rejected_account_id IS NULL AND receiver_rejected_at IS NULL
                AND decision_account_id IS NULL AND decided_at IS NULL
                AND (source_accepted_by_account_id IS NULL) = (source_accepted_at IS NULL)
                AND (source_rejected_by_account_id IS NULL) = (source_rejected_at IS NULL)
                AND (source_handover_confirmed_by_account_id IS NULL) = (source_handover_confirmed_at IS NULL)
                AND (source_accepted_at IS NULL OR source_accepted_at >= created_at)
                AND (source_rejected_at IS NULL OR source_rejected_at >= created_at)
                AND (source_handover_confirmed_at IS NULL OR (source_accepted_at IS NOT NULL AND source_handover_confirmed_at >= source_accepted_at))
                AND (status::text NOT IN ('APPROVED','COMPLETED') OR source_accepted_at IS NOT NULL)
                AND (status::text NOT IN ('PENDING_CONFIRMATION','CANCELLED','REJECTED') OR source_accepted_at IS NULL)
                AND (status::text <> 'REJECTED' OR (source_rejected_at IS NOT NULL AND source_rejection_reason IS NOT NULL AND btrim(source_rejection_reason) <> ''))
                AND (status::text = 'REJECTED' OR source_rejected_at IS NULL)
                AND (status::text <> 'COMPLETED' OR (source_handover_confirmed_at IS NOT NULL AND completed_at IS NOT NULL
                    AND target_handover_account_id IS NOT NULL AND completed_at >= source_handover_confirmed_at))
                AND status::text IN ('PENDING_CONFIRMATION','APPROVED','COMPLETED','REJECTED','CANCELLED')
            ))
            """));
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
        builder.Property(x => x.TransferType).HasColumnName("transfer_type").HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ReceiverConfirmedAccountId).HasColumnName("receiver_confirmed_account_id");
        builder.Property(x => x.ReceiverConfirmedAt).HasColumnName("receiver_confirmed_at");
        builder.Property(x => x.ReceiverRejectedAccountId).HasColumnName("receiver_rejected_account_id");
        builder.Property(x => x.ReceiverRejectedAt).HasColumnName("receiver_rejected_at");
        builder.Property(x => x.ReceiverRejectionReason).HasColumnName("receiver_rejection_reason").HasMaxLength(1000);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.ReceiverConfirmedAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.ReceiverRejectedAccountId).OnDelete(DeleteBehavior.Restrict);
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
        builder.Property(x => x.ConditionAtSourceHandover).HasColumnName("condition_at_source_handover").HasColumnType("device_condition");
        builder.Property(x => x.SourceHandoverNote).HasColumnName("source_handover_note").HasMaxLength(1000);
        builder.ToTable(t => t.HasCheckConstraint("ck_transfer_source_condition", "condition_at_source_handover IS NULL OR condition_at_source_handover IN ('GOOD','DAMAGED')"));
        builder.HasIndex(x => new { x.TransferId, x.DeviceId }).IsUnique().HasDatabaseName("uq_device_transfer_items_transfer_device");
        builder.HasIndex(x => x.DeviceId).HasDatabaseName("ix_device_transfer_items_device");
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
    }
}
