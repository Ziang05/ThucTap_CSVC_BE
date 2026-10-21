using CSVC.Core.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class AuthLoginThrottleConfiguration : IEntityTypeConfiguration<AuthLoginThrottle>
{
    public void Configure(EntityTypeBuilder<AuthLoginThrottle> builder)
    {
        builder.ToTable("auth_login_throttles", table =>
        {
            table.HasCheckConstraint("ck_auth_login_throttles_failed_count", "failed_count >= 0");
            table.HasCheckConstraint("ck_auth_login_throttles_email", "email_key <> '' AND email_key = lower(btrim(email_key)) AND email_key !~ '[[:space:]]'");
        });
        builder.HasKey(entity => entity.EmailKey);
        builder.Property(entity => entity.EmailKey).HasColumnName("email_key").HasColumnType("varchar(320)").HasMaxLength(320).IsRequired().ValueGeneratedNever();
        builder.Property(entity => entity.FailedCount).HasColumnName("failed_count").HasColumnType("integer").IsRequired().HasDefaultValue(0);
        builder.Property(entity => entity.FirstFailedAt).HasColumnName("first_failed_at").HasColumnType("timestamptz");
        builder.Property(entity => entity.BlockedUntil).HasColumnName("blocked_until").HasColumnType("timestamptz");
    }
}
