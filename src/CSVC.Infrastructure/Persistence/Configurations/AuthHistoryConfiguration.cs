using CSVC.Core.Accounts;
using CSVC.Core.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class AuthHistoryConfiguration : IEntityTypeConfiguration<AuthHistory>
{
    public void Configure(EntityTypeBuilder<AuthHistory> builder)
    {
        builder.ToTable("auth_history");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        builder.Property(entity => entity.RequestId).HasColumnName("request_id").HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ActorAccountId).HasColumnName("actor_account_id").HasColumnType("uuid");
        builder.Property(entity => entity.ActionCode).HasColumnName("action_code").HasColumnType("varchar(64)").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Outcome).HasColumnName("outcome").HasColumnType("audit_outcome").IsRequired();
        builder.Property(entity => entity.ReasonCode).HasColumnName("reason_code").HasColumnType("varchar(80)").HasMaxLength(80);
        builder.Property(entity => entity.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(entity => entity.AccountId).HasColumnName("account_id").HasColumnType("uuid");
        builder.Property(entity => entity.SessionId).HasColumnName("session_id").HasColumnType("uuid");
        builder.Property(entity => entity.EmailSubmitted).HasColumnName("email_submitted").HasColumnType("varchar(320)").HasMaxLength(320);
        builder.Property(entity => entity.BlockedUntil).HasColumnName("blocked_until").HasColumnType("timestamptz");
        builder.Property(entity => entity.IpAddress).HasColumnName("ip_address").HasColumnType("inet");
        builder.Property(entity => entity.UserAgent).HasColumnName("user_agent").HasColumnType("text");
        builder.HasIndex(entity => new { entity.RequestId, entity.ActionCode, entity.SessionId }).HasDatabaseName("ix_auth_history_correlation");
        builder.HasIndex(entity => new { entity.AccountId, entity.OccurredAt }).HasDatabaseName("ix_auth_history_account");
        builder.HasIndex(entity => new { entity.SessionId, entity.OccurredAt }).HasDatabaseName("ix_auth_history_session");
        builder.HasIndex(entity => new { entity.EmailSubmitted, entity.OccurredAt }).HasDatabaseName("ix_auth_history_email");
        builder.HasOne<Account>().WithMany().HasForeignKey(entity => entity.ActorAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(entity => entity.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AuthSession>().WithMany().HasForeignKey(entity => entity.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entity => entity.SessionId).IsUnique()
            .HasFilter("action_code = 'SESSION_ENDED'")
            .HasDatabaseName("uq_auth_history_session_ended");
    }
}
