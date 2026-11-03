using CSVC.Core.Accounts;
using CSVC.Core.Departments;
using CSVC.Core.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class EquipmentSettingsConfiguration : IEntityTypeConfiguration<EquipmentSettings>
{
    public void Configure(EntityTypeBuilder<EquipmentSettings> b)
    {
        b.ToTable("equipment_settings", t => t.HasCheckConstraint("ck_equipment_settings_singleton", "id = 1"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.EquipmentDepartmentId).HasColumnName("equipment_department_id");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.UpdatedByAccountId).HasColumnName("updated_by_account_id");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasOne<Department>().WithMany().HasForeignKey(x => x.EquipmentDepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.UpdatedByAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasData(new EquipmentSettings());
    }
}

public sealed class EquipmentSettingsHistoryConfiguration : IEntityTypeConfiguration<EquipmentSettingsHistory>
{
    public void Configure(EntityTypeBuilder<EquipmentSettingsHistory> b)
    {
        b.ToTable("equipment_settings_history");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.RequestId).HasColumnName("request_id");
        b.Property(x => x.ActorAccountId).HasColumnName("actor_account_id");
        b.Property(x => x.PreviousDepartmentId).HasColumnName("previous_department_id");
        b.Property(x => x.DepartmentId).HasColumnName("department_id");
        b.Property(x => x.OccurredAt).HasColumnName("occurred_at");
        b.HasIndex(x => x.RequestId).IsUnique();
        b.HasIndex(x => x.OccurredAt);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Department>().WithMany().HasForeignKey(x => x.PreviousDepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
