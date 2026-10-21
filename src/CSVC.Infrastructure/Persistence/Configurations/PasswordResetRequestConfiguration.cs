using CSVC.Core.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class PasswordResetRequestConfiguration : IEntityTypeConfiguration<PasswordResetRequest>
{
    public void Configure(EntityTypeBuilder<PasswordResetRequest> builder)
    {
        builder.ToTable("password_reset_requests");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        builder.Property(entity => entity.AccountId).HasColumnName("account_id").HasColumnType("uuid").IsRequired();
        builder.HasIndex(entity => entity.AccountId).IsUnique().HasFilter("NOT is_closed")
            .HasDatabaseName("uq_password_reset_requests_open_account");
        builder.Property(entity => entity.TokenHash).HasColumnName("token_hash").HasColumnType("varchar(128)").HasMaxLength(128).IsRequired();
        builder.HasIndex(entity => entity.TokenHash).IsUnique();
        builder.Property(entity => entity.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(entity => entity.IsClosed).HasColumnName("is_closed").HasColumnType("boolean").IsRequired().HasDefaultValue(false);
        builder.HasOne<Account>().WithMany().HasForeignKey(entity => entity.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
