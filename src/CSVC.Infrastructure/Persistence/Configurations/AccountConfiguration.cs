using CSVC.Core.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", table =>
        {
            table.HasCheckConstraint("ck_accounts_email", "normalized_email <> '' AND normalized_email !~ '[[:space:]]'");
            table.HasCheckConstraint("ck_accounts_code", "btrim(account_code) <> ''");
            table.HasCheckConstraint("ck_accounts_password_hash", "btrim(password_hash) <> ''");
        });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        builder.Property(entity => entity.AccountCode).HasColumnName("account_code").HasColumnType("varchar(32)").HasMaxLength(32).IsRequired();
        builder.HasIndex(entity => entity.AccountCode).IsUnique();
        builder.Property<string>("NormalizedCode").HasColumnName("normalized_code").HasColumnType("text")
            .HasComputedColumnSql("lower(btrim(account_code))", stored: true).IsRequired();
        builder.HasIndex("NormalizedCode").IsUnique().HasDatabaseName("uq_accounts_normalized_code");
        builder.Property(entity => entity.UserProfileId).HasColumnName("user_profile_id").HasColumnType("uuid").IsRequired();
        builder.HasIndex(entity => entity.UserProfileId).IsUnique();
        builder.Property(entity => entity.RoleId).HasColumnName("role_id").HasColumnType("smallint").IsRequired();
        builder.Property(entity => entity.Email).HasColumnName("email").HasColumnType("varchar(320)").HasMaxLength(320).IsRequired();
        builder.Property<string>("NormalizedEmail").HasColumnName("normalized_email").HasColumnType("text")
            .HasComputedColumnSql("lower(btrim(email))", stored: true).IsRequired();
        builder.HasIndex("NormalizedEmail").IsUnique().HasDatabaseName("uq_accounts_normalized_email");
        builder.Property(entity => entity.PasswordHash).HasColumnName("password_hash").HasColumnType("text").IsRequired();
        builder.Property(entity => entity.IsLock).HasColumnName("is_lock").HasColumnType("boolean").IsRequired().HasDefaultValue(false);
        builder.HasOne<UserProfile>().WithOne().HasForeignKey<Account>(entity => entity.UserProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Role>().WithMany().HasForeignKey(entity => entity.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}
