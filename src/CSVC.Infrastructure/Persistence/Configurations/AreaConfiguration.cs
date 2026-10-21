using CSVC.Core.Areas;
using CSVC.Core.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class AreaConfiguration : IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<Area> builder)
    {
        builder.ToTable("areas", table =>
        {
            table.HasCheckConstraint("ck_areas_name", "normalized_name <> ''");
            table.HasCheckConstraint("ck_areas_code", "btrim(code) <> ''");
            table.HasCheckConstraint("ck_areas_not_self_parent", "parent_id IS NULL OR parent_id <> id");
            table.HasTrigger("trg_areas_write_lock");
            table.HasTrigger("trg_areas_no_cycle");
        });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        builder.Property(entity => entity.Code).HasColumnName("code").HasColumnType("varchar(32)").HasMaxLength(32).IsRequired();
        builder.HasIndex(entity => entity.Code).IsUnique();
        builder.Property(entity => entity.ParentId).HasColumnName("parent_id").HasColumnType("uuid");
        builder.Property(entity => entity.Name).HasColumnName("name").HasColumnType("varchar(200)").HasMaxLength(200).IsRequired();
        builder.Property<string>("NormalizedName").HasColumnName("normalized_name").HasColumnType("text")
            .HasComputedColumnSql("lower(btrim(regexp_replace(normalize(name, NFC), '[[:space:]]+', ' ', 'g')))", stored: true).IsRequired();
        builder.HasIndex("NormalizedName").IsUnique().HasFilter("parent_id IS NULL")
            .HasDatabaseName("uq_areas_root_normalized_name");
        builder.HasIndex("ParentId", "NormalizedName").IsUnique().HasFilter("parent_id IS NOT NULL")
            .HasDatabaseName("uq_areas_sibling_normalized_name");
        builder.Property(entity => entity.TypeLabel).HasColumnName("type_label").HasColumnType("varchar(100)").HasMaxLength(100);
        builder.Property(entity => entity.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(entity => entity.Status).HasColumnName("status").HasColumnType("entity_status").IsRequired().HasDefaultValue(EntityStatus.Active);
        builder.HasOne<Area>().WithMany().HasForeignKey(entity => entity.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}
