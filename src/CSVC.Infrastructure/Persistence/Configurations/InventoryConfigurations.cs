using CSVC.Core.Accounts;
using CSVC.Core.Areas;
using CSVC.Core.Departments;
using CSVC.Core.Devices;
using CSVC.Core.Inventories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.NameTranslation;

namespace CSVC.Infrastructure.Persistence.Configurations;

internal static class InventoryMapping
{
    public static void Columns<T>(EntityTypeBuilder<T> b) where T : class
    {
        var names = new NpgsqlSnakeCaseNameTranslator();
        foreach (var p in b.Metadata.GetProperties()) p.SetColumnName(names.TranslateMemberName(p.Name));
        b.HasKey("Id"); b.Property("Id").ValueGeneratedNever();
    }
    public static void Accounts<T>(EntityTypeBuilder<T> b, params string[] properties) where T : class
    {
        foreach (var property in properties) b.HasOne<Account>().WithMany().HasForeignKey(property).OnDelete(DeleteBehavior.Restrict);
    }
    public static void Receipt<T>(EntityTypeBuilder<T> b) where T : class
    {
        b.Property<string>("RequestHash").HasMaxLength(64);
        b.Property<string>("ResponseData").HasColumnType("jsonb");
        b.HasIndex("RequestId").IsUnique();
    }
}

public sealed class InventorySlipConfiguration : IEntityTypeConfiguration<InventorySlip>
{
    public void Configure(EntityTypeBuilder<InventorySlip> b)
    {
        b.ToTable("inventory_slips", t =>
        {
            t.HasCheckConstraint("ck_inv_slip_status", "status IN ('PENDING_APPROVAL','PREPARING','IN_PROGRESS','COMPLETED','CANCELED')");
            t.HasCheckConstraint("ck_inv_slip_version", "version >= 0");
            t.HasCheckConstraint("ck_inv_cancel_reason", "status <> 'CANCELED' OR (cancel_reason IS NOT NULL AND btrim(cancel_reason) <> '')");
        });
        b.Property(x => x.DisplayCode).HasMaxLength(64); b.HasIndex(x => x.DisplayCode).IsUnique();
        b.HasIndex(x => x.CreateRequestId).IsUnique(); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.DepartmentId, x.Status, x.CreatedAt });
        b.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Revisions).WithOne().HasForeignKey(x => x.SlipId).OnDelete(DeleteBehavior.Restrict);
        InventoryMapping.Accounts(b, nameof(InventorySlip.CreatedByAccountId), nameof(InventorySlip.ApprovedByAccountId),
            nameof(InventorySlip.StartedByAccountId), nameof(InventorySlip.CompletedByAccountId), nameof(InventorySlip.CanceledByAccountId));
        InventoryMapping.Columns(b);
    }
}
public sealed class InventoryPlanRevisionConfiguration : IEntityTypeConfiguration<InventoryPlanRevision>
{
    public void Configure(EntityTypeBuilder<InventoryPlanRevision> b)
    {
        b.ToTable("inventory_plan_revisions", t =>
        {
            t.HasCheckConstraint("ck_inv_revision_state", "status IN ('DRAFT','PUBLISHED','REJECTED') AND revision_no >= 1 AND version >= 0");
            t.HasCheckConstraint("ck_inv_revision_time", "planned_end_at IS NULL OR planned_end_at > planned_start_at");
            t.HasCheckConstraint("ck_inv_revision_reason", "revision_no = 1 OR (change_reason IS NOT NULL AND btrim(change_reason) <> '')");
            t.HasCheckConstraint("ck_inv_revision_notice", "status <> 'PUBLISHED' OR (approved_at IS NOT NULL AND approved_by_account_id IS NOT NULL AND published_at IS NOT NULL AND notice_days_snapshot >= 1 AND notice_days_snapshot IS NOT NULL AND planned_start_at >= published_at + notice_days_snapshot * interval '24 hours')");
        });
        b.Property(x => x.DiffData).HasColumnType("jsonb"); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.SlipId, x.RevisionNo }).IsUnique();
        b.HasIndex(x => x.SlipId).IsUnique().HasFilter("status = 'DRAFT'").HasDatabaseName("uq_inv_one_draft_per_slip");
        b.HasIndex(x => new { x.Status, x.PlannedStartAt });
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Assignees).WithOne().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
        InventoryMapping.Accounts(b, nameof(InventoryPlanRevision.ProposedByAccountId), nameof(InventoryPlanRevision.ApprovedByAccountId), nameof(InventoryPlanRevision.RejectedByAccountId));
        InventoryMapping.Columns(b);
    }
}
public sealed class InventoryPlanItemConfiguration : IEntityTypeConfiguration<InventoryPlanItem>
{
    public void Configure(EntityTypeBuilder<InventoryPlanItem> b)
    {
        b.ToTable("inventory_plan_items", t => t.HasCheckConstraint("ck_inv_item_quantity", "quantity_snapshot >= 0"));
        b.HasIndex(x => new { x.RevisionId, x.DeviceId }).IsUnique();
        b.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentIdSnapshot).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Area>().WithMany().HasForeignKey(x => x.AreaIdSnapshot).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Result).WithOne().HasForeignKey<InventoryResult>(x => x.PlanItemId).OnDelete(DeleteBehavior.Restrict);
        InventoryMapping.Columns(b);
    }
}
public sealed class InventoryRevisionAssigneeConfiguration : IEntityTypeConfiguration<InventoryRevisionAssignee>
{
    public void Configure(EntityTypeBuilder<InventoryRevisionAssignee> b)
    {
        b.ToTable("inventory_revision_assignees"); b.HasIndex(x => new { x.RevisionId, x.AccountId }).IsUnique();
        InventoryMapping.Accounts(b, nameof(InventoryRevisionAssignee.AccountId), nameof(InventoryRevisionAssignee.AssignedByAccountId));
        InventoryMapping.Columns(b);
    }
}
public sealed class InventoryResultConfiguration : IEntityTypeConfiguration<InventoryResult>
{
    public void Configure(EntityTypeBuilder<InventoryResult> b)
    {
        b.ToTable("inventory_results", t =>
        {
            t.HasCheckConstraint("ck_inv_result_version", "version >= 0");
            t.HasCheckConstraint("ck_inv_result_editor", "(last_corrected_by_account_id IS NULL) = (last_corrected_at IS NULL)");
        });
        b.HasIndex(x => x.PlanItemId).IsUnique(); b.HasIndex(x => x.InitialRequestId).IsUnique();
        b.HasIndex(x => new { x.CheckedByAccountId, x.CheckedAt }); b.Property(x => x.Version).IsConcurrencyToken();
        InventoryMapping.Accounts(b, nameof(InventoryResult.CheckedByAccountId), nameof(InventoryResult.LastCorrectedByAccountId));
        InventoryMapping.Columns(b);
    }
}
public sealed class InventoryItemHistoryConfiguration : IEntityTypeConfiguration<InventoryItemHistory>
{
    public void Configure(EntityTypeBuilder<InventoryItemHistory> b)
    {
        b.ToTable("inventory_item_history", t => t.HasCheckConstraint("ck_inv_item_history_shape", "(action_code = 'RESULT_RECORDED' AND before_exists IS NULL AND version_before IS NULL AND version_after = 0 AND reason IS NULL) OR (action_code = 'RESULT_CORRECTED' AND before_exists IS NOT NULL AND before_exists <> after_exists AND version_before IS NOT NULL AND version_before >= 0 AND version_after = version_before + 1 AND reason IS NOT NULL AND btrim(reason) <> '')"));
        b.HasOne<InventoryResult>().WithMany().HasForeignKey(x => x.ResultId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ResultId, x.VersionAfter }).IsUnique();
        InventoryMapping.Accounts(b, nameof(InventoryItemHistory.ActorAccountId)); InventoryMapping.Receipt(b); InventoryMapping.Columns(b);
    }
}
public sealed class InventoryHistoryConfiguration : IEntityTypeConfiguration<InventoryHistory>
{
    public void Configure(EntityTypeBuilder<InventoryHistory> b)
    {
        b.ToTable("inventory_history", t => t.HasCheckConstraint("ck_inv_history_action", "action_code IN ('CREATE','UPDATE_PLAN','PROPOSE_PLAN','PUBLISH','REJECT_PLAN','START','COMPLETE','CANCEL') AND (target_key = 'SLIP' OR target_key LIKE 'REVISION:%')"));
        b.HasOne<InventorySlip>().WithMany().HasForeignKey(x => x.SlipId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.BeforeData).HasColumnType("jsonb"); b.Property(x => x.AfterData).HasColumnType("jsonb");
        b.HasIndex(x => new { x.SlipId, x.OccurredAt, x.Id });
        InventoryMapping.Accounts(b, nameof(InventoryHistory.ActorAccountId)); InventoryMapping.Receipt(b); InventoryMapping.Columns(b);
    }
}
public sealed class InventoryNoticePolicyConfiguration : IEntityTypeConfiguration<InventoryNoticePolicy>
{
    public void Configure(EntityTypeBuilder<InventoryNoticePolicy> b)
    {
        b.ToTable("inventory_notice_policies", t => t.HasCheckConstraint("ck_inv_policy", "id = 1 AND minimum_notice_days BETWEEN 1 AND 365 AND version >= 0"));
        b.Property(x => x.Version).IsConcurrencyToken(); InventoryMapping.Accounts(b, nameof(InventoryNoticePolicy.UpdatedByAccountId));
        InventoryMapping.Columns(b);
        b.HasData(new InventoryNoticePolicy { Id = 1, MinimumNoticeDays = 3, UpdatedAt = new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero) });
    }
}
public sealed class InventoryNoticePolicyHistoryConfiguration : IEntityTypeConfiguration<InventoryNoticePolicyHistory>
{
    public void Configure(EntityTypeBuilder<InventoryNoticePolicyHistory> b)
    {
        b.ToTable("inventory_notice_policy_history", t => t.HasCheckConstraint("ck_inv_policy_change", "previous_days BETWEEN 1 AND 365 AND new_days BETWEEN 1 AND 365 AND previous_days <> new_days AND btrim(reason) <> ''"));
        b.HasOne<InventoryNoticePolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        InventoryMapping.Accounts(b, nameof(InventoryNoticePolicyHistory.ChangedByAccountId)); InventoryMapping.Receipt(b); InventoryMapping.Columns(b);
    }
}
public sealed class InventoryNotificationConfiguration : IEntityTypeConfiguration<InventoryNotification>
{
    public void Configure(EntityTypeBuilder<InventoryNotification> b)
    {
        b.ToTable("inventory_notifications", t =>
        {
            t.HasCheckConstraint("ck_inv_notification", "attempts >= 0 AND event_type IN ('PLAN_PUBLISHED','PLAN_UPDATED','PLAN_CANCELED','REMINDER') AND delivery_status IN ('PENDING','SENT','FAILED')");
        });
        b.Property(x => x.EventKey).HasMaxLength(180); b.HasIndex(x => x.EventKey).IsUnique();
        b.Property(x => x.PayloadSnapshot).HasColumnType("jsonb");
        b.HasIndex(x => new { x.RecipientAccountId, x.PublishedAt, x.Id });
        b.HasOne<InventorySlip>().WithMany().HasForeignKey(x => x.SlipId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryPlanRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
        InventoryMapping.Accounts(b, nameof(InventoryNotification.RecipientAccountId)); InventoryMapping.Columns(b);
    }
}
