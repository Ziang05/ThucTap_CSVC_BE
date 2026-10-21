using CSVC.Core.Common;
using CSVC.Core.Departments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("departments", table =>
        {
            table.HasCheckConstraint("ck_departments_name", "normalized_name <> ''");
            table.HasCheckConstraint("ck_departments_code", "btrim(code) <> ''");
        });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        builder.Property(entity => entity.Code).HasColumnName("code").HasColumnType("varchar(32)").HasMaxLength(32).IsRequired();
        builder.HasIndex(entity => entity.Code).IsUnique();
        builder.Property(entity => entity.Name).HasColumnName("name").HasColumnType("varchar(200)").HasMaxLength(200).IsRequired();
        builder.Property<string>("NormalizedName").HasColumnName("normalized_name").HasColumnType("text")
            .HasComputedColumnSql("lower(btrim(regexp_replace(normalize(name, NFC), '[[:space:]]+', ' ', 'g')))", stored: true).IsRequired();
        builder.HasIndex("NormalizedName").IsUnique().HasDatabaseName("uq_departments_normalized_name");
        builder.Property(entity => entity.ShortName).HasColumnName("short_name").HasColumnType("varchar(50)").HasMaxLength(50);
        builder.Property(entity => entity.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(entity => entity.Status).HasColumnName("status").HasColumnType("entity_status").IsRequired().HasDefaultValue(EntityStatus.Active);
    }
}
