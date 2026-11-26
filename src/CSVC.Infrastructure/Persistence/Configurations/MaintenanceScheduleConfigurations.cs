using CSVC.Core.DeviceCategories;
using CSVC.Core.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class DeviceMaintenanceScheduleConfiguration : IEntityTypeConfiguration<DeviceMaintenanceSchedule>
{
    public void Configure(EntityTypeBuilder<DeviceMaintenanceSchedule> b)
    {
        b.ToTable("device_maintenance_schedules", t =>
            t.HasCheckConstraint("ck_maintenance_schedules_version", "version > 0"));
        b.HasKey(x => x.DeviceId);
        b.Property(x => x.DeviceId).ValueGeneratedNever();
        b.Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken();
        b.HasOne<Device>().WithOne().HasForeignKey<DeviceMaintenanceSchedule>(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        ServiceMapping.Account(b, nameof(DeviceMaintenanceSchedule.CreatedByAccountId));
        ServiceMapping.Columns(b);
    }
}

public sealed class DeviceMaintenancePeriodConfiguration : IEntityTypeConfiguration<DeviceMaintenancePeriod>
{
    public void Configure(EntityTypeBuilder<DeviceMaintenancePeriod> b)
    {
        b.ToTable("device_maintenance_periods", t =>
        {
            t.HasCheckConstraint("ck_maintenance_periods_interval", "interval_months > 0");
            t.HasCheckConstraint("ck_maintenance_periods_status", "status IN ('Scheduled', 'Due', 'Completed', 'Missed')");
            t.HasCheckConstraint("ck_maintenance_periods_window",
                "(status = 'Scheduled' AND closes_on IS NULL) OR (status <> 'Scheduled' AND closes_on IS NOT NULL AND closes_on > due_on)");
            t.HasCheckConstraint("ck_maintenance_periods_completion", "(status = 'Completed') = (completed_by_item_id IS NOT NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).HasDefaultValue(MaintenancePeriodStatus.Scheduled);
        b.HasAlternateKey(x => new { x.Id, x.DeviceId }).HasName("uq_maintenance_periods_id_device");
        b.HasIndex(x => new { x.DeviceId, x.DueOn }).IsUnique().HasDatabaseName("uq_maintenance_periods_device_due");
        b.HasIndex(x => x.DeviceId, "ScheduledDevice").IsUnique().HasDatabaseName("uq_maintenance_periods_one_scheduled")
            .HasFilter("status = 'Scheduled'");
        b.HasIndex(x => x.DeviceId, "DueDevice").IsUnique().HasDatabaseName("uq_maintenance_periods_one_due")
            .HasFilter("status = 'Due'");
        b.HasIndex(x => new { x.Status, x.DueOn }).HasDatabaseName("ix_maintenance_periods_status_due");
        b.HasIndex(x => new { x.Status, x.ClosesOn }).HasDatabaseName("ix_maintenance_periods_status_close");
        b.HasOne<DeviceMaintenanceSchedule>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeviceCategory>().WithMany().HasForeignKey(x => x.IntervalSourceCategoryId).OnDelete(DeleteBehavior.Restrict);
        // The completion FK uses the nullable service item's period as part of its principal key and is added in SQL.
        ServiceMapping.Columns(b);
    }
}
