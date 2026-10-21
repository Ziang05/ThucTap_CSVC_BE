using CSVC.Core.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class AccountHistoryConfiguration : IEntityTypeConfiguration<AccountHistory>
{
    public void Configure(EntityTypeBuilder<AccountHistory> builder)
    {
        builder.ToTable("account_history");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        builder.Property(entity => entity.RequestId).HasColumnName("request_id").HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ActorAccountId).HasColumnName("actor_account_id").HasColumnType("uuid");
        builder.Property(entity => entity.ActionCode).HasColumnName("action_code").HasColumnType("varchar(64)").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Outcome).HasColumnName("outcome").HasColumnType("audit_outcome").IsRequired();
        builder.Property(entity => entity.ReasonCode).HasColumnName("reason_code").HasColumnType("varchar(80)").HasMaxLength(80);
        builder.Property(entity => entity.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(entity => entity.TargetAccountId).HasColumnName("target_account_id").HasColumnType("uuid");
        builder.Property(entity => entity.ChangedFields).HasColumnName("changed_fields").HasColumnType("jsonb");
        builder.Property(entity => entity.BeforeData).HasColumnName("before_data").HasColumnType("jsonb");
        builder.Property(entity => entity.AfterData).HasColumnName("after_data").HasColumnType("jsonb");
        builder.HasIndex(entity => new { entity.RequestId, entity.ActionCode }).IsUnique().HasDatabaseName("uq_account_history_request_action");
        builder.HasIndex(entity => new { entity.TargetAccountId, entity.OccurredAt }).HasDatabaseName("ix_account_history_target");
        builder.HasIndex(entity => new { entity.ActorAccountId, entity.OccurredAt }).HasDatabaseName("ix_account_history_actor");
        builder.HasOne<Account>().WithMany().HasForeignKey(entity => entity.ActorAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(entity => entity.TargetAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
