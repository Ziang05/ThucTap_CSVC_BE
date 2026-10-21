using CSVC.Core.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("smallint").IsRequired().ValueGeneratedNever();
        builder.Property(entity => entity.Code).HasColumnName("code").HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.HasIndex(entity => entity.Code).IsUnique();
        builder.Property(entity => entity.DisplayName).HasColumnName("display_name").HasColumnType("varchar(120)").HasMaxLength(120).IsRequired();
        builder.HasData(
            new Role { Id = 1, Code = RoleCodes.EquipmentManager, DisplayName = "Trưởng phòng QTTB" },
            new Role { Id = 2, Code = RoleCodes.EquipmentStaff, DisplayName = "Nhân viên phòng QTTB" },
            new Role { Id = 3, Code = RoleCodes.DepartmentStaff, DisplayName = "Nhân viên phòng ban" });
    }
}
