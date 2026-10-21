using CSVC.Core.Accounts;
using CSVC.Core.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("auth_sessions");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        builder.Property(entity => entity.AccountId).HasColumnName("account_id").HasColumnType("uuid").IsRequired();
        builder.HasIndex(entity => entity.AccountId).IsUnique().HasFilter("is_active")
            .HasDatabaseName("uq_auth_sessions_active_account");
        builder.Property(entity => entity.SessionTokenHash).HasColumnName("session_token_hash").HasColumnType("varchar(128)").HasMaxLength(128).IsRequired();
        builder.HasIndex(entity => entity.SessionTokenHash).IsUnique();
        builder.Property(entity => entity.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(entity => entity.IsActive).HasColumnName("is_active").HasColumnType("boolean").IsRequired().HasDefaultValue(true);
        builder.HasOne<Account>().WithMany().HasForeignKey(entity => entity.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
