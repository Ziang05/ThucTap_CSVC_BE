using CSVC.Core.Common;
using CSVC.Core.DeviceCategories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class DeviceCategoryConfiguration : IEntityTypeConfiguration<DeviceCategory>
{
    public void Configure(EntityTypeBuilder<DeviceCategory> builder)
    {
        builder.ToTable("device_categories", table =>
        {
            table.HasCheckConstraint("ck_device_categories_name", "normalized_name <> ''");
            table.HasCheckConstraint("ck_device_categories_display_code", "btrim(display_code) <> ''");
        });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        builder.Property(entity => entity.DisplayCode).HasColumnName("display_code").HasColumnType("varchar(64)").HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => entity.DisplayCode).IsUnique();
        builder.Property<string>("NormalizedDisplayCode").HasColumnName("normalized_display_code").HasColumnType("text")
            .HasComputedColumnSql("lower(btrim(display_code))", stored: true).IsRequired();
        builder.HasIndex("NormalizedDisplayCode").IsUnique().HasDatabaseName("uq_device_categories_normalized_display_code");
        builder.Property(entity => entity.Name).HasColumnName("name").HasColumnType("varchar(200)").HasMaxLength(200).IsRequired();
        builder.Property<string>("NormalizedName").HasColumnName("normalized_name").HasColumnType("text")
            .HasComputedColumnSql("lower(btrim(regexp_replace(normalize(name, NFC), '[[:space:]]+', ' ', 'g')))", stored: true).IsRequired();
        builder.HasIndex("NormalizedName").IsUnique().HasDatabaseName("uq_device_categories_normalized_name");
        builder.Property(entity => entity.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(entity => entity.Notes).HasColumnName("notes").HasColumnType("text");
        builder.Property(entity => entity.Status).HasColumnName("status").HasColumnType("entity_status").IsRequired().HasDefaultValue(EntityStatus.Active);
    }
}
