using CSVC.Core.Common;
using CSVC.Core.Manufacturers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class ManufacturerConfiguration : IEntityTypeConfiguration<Manufacturer>
{
    public void Configure(EntityTypeBuilder<Manufacturer> builder)
    {
        builder.ToTable("manufacturers", table =>
        {
            table.HasCheckConstraint("ck_manufacturers_name", "normalized_name <> ''");
            table.HasCheckConstraint("ck_manufacturers_display_code", "btrim(display_code) <> ''");
        });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        builder.Property(entity => entity.DisplayCode).HasColumnName("display_code").HasColumnType("varchar(64)").HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => entity.DisplayCode).IsUnique();
        builder.Property<string>("NormalizedDisplayCode").HasColumnName("normalized_display_code").HasColumnType("text")
            .HasComputedColumnSql("lower(btrim(display_code))", stored: true).IsRequired();
        builder.HasIndex("NormalizedDisplayCode").IsUnique().HasDatabaseName("uq_manufacturers_normalized_display_code");
        builder.Property(entity => entity.Name).HasColumnName("name").HasColumnType("varchar(200)").HasMaxLength(200).IsRequired();
        builder.Property<string>("NormalizedName").HasColumnName("normalized_name").HasColumnType("text")
            .HasComputedColumnSql("lower(btrim(regexp_replace(normalize(name, NFC), '[[:space:]]+', ' ', 'g')))", stored: true).IsRequired();
        builder.HasIndex("NormalizedName").IsUnique().HasDatabaseName("uq_manufacturers_normalized_name");
        builder.Property(entity => entity.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(entity => entity.Country).HasColumnName("country").HasColumnType("varchar(120)").HasMaxLength(120);
        builder.Property(entity => entity.Website).HasColumnName("website").HasColumnType("varchar(2048)").HasMaxLength(2048);
        builder.Property(entity => entity.ShortName).HasColumnName("short_name").HasColumnType("varchar(120)").HasMaxLength(120);
        builder.Property(entity => entity.ContactInfo).HasColumnName("contact_info").HasColumnType("text");
        builder.Property(entity => entity.Notes).HasColumnName("notes").HasColumnType("text");
        builder.Property(entity => entity.Status).HasColumnName("status").HasColumnType("entity_status").IsRequired().HasDefaultValue(EntityStatus.Active);
    }
}
