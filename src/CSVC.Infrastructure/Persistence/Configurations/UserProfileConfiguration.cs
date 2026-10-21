using CSVC.Core.Accounts;
using CSVC.Core.Departments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        builder.Property(entity => entity.DepartmentId).HasColumnName("department_id").HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.FullName).HasColumnName("full_name").HasColumnType("varchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.BirthDate).HasColumnName("birth_date").HasColumnType("date");
        builder.Property(entity => entity.Phone).HasColumnName("phone").HasColumnType("varchar(32)").HasMaxLength(32);
        builder.HasOne<Department>().WithMany().HasForeignKey(entity => entity.DepartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
