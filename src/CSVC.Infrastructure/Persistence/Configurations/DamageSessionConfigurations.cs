using CSVC.Core.DamageReports;
using CSVC.Core.Devices;
using CSVC.Core.ServiceOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class DeviceDamageSessionConfiguration : IEntityTypeConfiguration<DeviceDamageSession>
{
    public void Configure(EntityTypeBuilder<DeviceDamageSession> b)
    {
        b.ToTable("device_damage_sessions", t =>
        {
            t.HasCheckConstraint("ck_damage_session_version", "version > 0");
            t.HasCheckConstraint("ck_damage_session_closed", "(closed_at IS NULL) = (closed_by_account_id IS NULL) AND (closed_at IS NULL OR closed_at >= opened_at)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasAlternateKey(x => new { x.Id, x.DeviceId }).HasName("uq_device_damage_sessions_id_device");
        b.HasIndex(x => x.DeviceId, "OpenDamageSession").IsUnique().HasFilter("closed_at IS NULL")
            .HasDatabaseName("uq_device_damage_sessions_one_open");
        b.HasIndex(x => new { x.DeviceId, x.OpenedAt });
        b.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        ReportMapping.Account(b, nameof(DeviceDamageSession.OpenedByAccountId));
        ReportMapping.Account(b, nameof(DeviceDamageSession.ClosedByAccountId));
        ReportMapping.Columns(b);
    }
}

public sealed class DamageReportDecisionConfiguration : IEntityTypeConfiguration<DamageReportDecision>
{
    public void Configure(EntityTypeBuilder<DamageReportDecision> b)
    {
        b.ToTable("damage_report_decisions", t => t.HasCheckConstraint("ck_damage_report_decision_outcome",
            "(outcome = 'ConfirmedDamaged' AND damage_session_id IS NOT NULL OR outcome = 'ConfirmedNotDamaged' AND damage_session_id IS NULL) AND btrim(reason) <> ''"));
        b.HasKey(x => x.DamageReportId);
        b.Property(x => x.DamageReportId).ValueGeneratedNever();
        b.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.HasIndex(x => x.Outcome);
        b.HasOne<DeviceDamageSession>().WithMany().HasForeignKey(x => new { x.DamageSessionId, x.DeviceId })
            .HasPrincipalKey(x => new { x.Id, x.DeviceId }).OnDelete(DeleteBehavior.Restrict);
        ReportMapping.Account(b, nameof(DamageReportDecision.DecidedByAccountId));
        ReportMapping.Columns(b);
    }
}

public sealed class DamageSessionInspectionConfiguration : IEntityTypeConfiguration<DamageSessionInspection>
{
    public void Configure(EntityTypeBuilder<DamageSessionInspection> b)
    {
        b.ToTable("damage_session_inspections", t => t.HasCheckConstraint("ck_damage_session_inspection",
            "expected_version > 0 AND btrim(notes) <> '' AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid"));
        b.HasKey(x => x.RequestId);
        b.Property(x => x.RequestId).ValueGeneratedNever();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => x.ServiceOrderItemId).IsUnique();
        b.HasIndex(x => x.DamageSessionId, "PassedInspection").IsUnique().HasFilter("passed")
            .HasDatabaseName("uq_damage_session_one_passing_inspection");
        b.HasOne<DeviceDamageSession>().WithMany().HasForeignKey(x => x.DamageSessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ServiceOrderItem>().WithMany().HasForeignKey(x => x.ServiceOrderItemId).OnDelete(DeleteBehavior.Restrict);
        ReportMapping.Account(b, nameof(DamageSessionInspection.InspectedByAccountId));
        ReportMapping.Columns(b);
    }
}
