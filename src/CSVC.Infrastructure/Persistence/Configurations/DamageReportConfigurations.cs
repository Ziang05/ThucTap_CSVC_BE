using CSVC.Core.Accounts;
using CSVC.Core.Areas;
using CSVC.Core.DamageReports;
using CSVC.Core.Departments;
using CSVC.Core.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.NameTranslation;

namespace CSVC.Infrastructure.Persistence.Configurations;

internal static class ReportMapping
{
    public static void Columns<T>(EntityTypeBuilder<T> b) where T : class
    {
        var names = new NpgsqlSnakeCaseNameTranslator();
        foreach (var p in b.Metadata.GetProperties()) p.SetColumnName(names.TranslateMemberName(p.Name));
    }
    public static void Account<T>(EntityTypeBuilder<T> b, string property) where T : class
        => b.HasOne<Account>().WithMany().HasForeignKey(property).OnDelete(DeleteBehavior.Restrict);
}

public sealed class DamageReportConfiguration : IEntityTypeConfiguration<DamageReport>
{
    public void Configure(EntityTypeBuilder<DamageReport> b)
    {
        b.ToTable("damage_reports", t =>
        {
            t.HasCheckConstraint("ck_damage_report_content", "btrim(report_code) <> '' AND btrim(incident_description) <> '' AND length(incident_description) <= 10000 AND (notes IS NULL OR length(notes) <= 4000)");
            t.HasCheckConstraint("ck_damage_report_time_version", "detected_at <= reported_at AND row_version > 0 AND (last_updated_at IS NULL OR last_updated_at >= reported_at)");
            t.HasCheckConstraint("ck_damage_report_editor", "(last_updated_at IS NULL) = (last_updated_by_account_id IS NULL)");
            t.HasCheckConstraint("ck_damage_report_hash", "create_request_hash ~ '^[0-9A-F]{64}$' AND create_request_id <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ReportCode).HasMaxLength(32);
        b.Property(x => x.CreateRequestHash).HasMaxLength(64);
        b.Property(x => x.IncidentType).HasColumnType("damage_report_incident_type");
        b.Property(x => x.RowVersion).HasDefaultValue(1).IsConcurrencyToken();
        b.HasIndex(x => x.ReportCode).IsUnique(); b.HasIndex(x => x.CreateRequestId).IsUnique();
        // A unique index (not an EF alternate key) permits changing DeviceId. The deferred composite FK is installed by migration SQL.
        b.HasIndex(x => new { x.Id, x.DeviceId }).IsUnique().HasDatabaseName("uq_damage_reports_id_device_for_pending_fk");
        b.HasIndex(x => new { x.ReportingDepartmentId, x.ReportedAt, x.Id });
        b.HasIndex(x => new { x.DeviceId, x.ReportedAt, x.Id });
        b.HasIndex(x => new { x.ReporterAccountId, x.ReportedAt });
        b.HasIndex(x => new { x.IncidentType, x.ReportedAt });
        b.HasIndex(x => new { x.ReportedAt, x.Id });
        b.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Department>().WithMany().HasForeignKey(x => x.ReportingDepartmentId).OnDelete(DeleteBehavior.Restrict);
        ReportMapping.Account(b, nameof(DamageReport.ReporterAccountId));
        ReportMapping.Account(b, nameof(DamageReport.LastUpdatedByAccountId));
        b.HasOne(x => x.Context).WithOne().HasForeignKey<DamageReportContext>(x => x.DamageReportId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Pending).WithOne().HasForeignKey<DamageReportPendingDevice>(x => x.DamageReportId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Reception).WithOne().HasForeignKey<DamageReportReception>(x => x.DamageReportId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Evidence).WithOne().HasForeignKey(x => x.DamageReportId).OnDelete(DeleteBehavior.Restrict);
        ReportMapping.Columns(b);
    }
}
public sealed class DamageReportContextConfiguration : IEntityTypeConfiguration<DamageReportContext>
{
    public void Configure(EntityTypeBuilder<DamageReportContext> b)
    {
        b.ToTable("damage_report_contexts"); b.HasKey(x => x.DamageReportId); b.Property(x => x.DamageReportId).ValueGeneratedNever();
        // Snapshot limits follow the existing source columns.
        b.Property(x => x.ReporterNameSnapshot).HasMaxLength(200); b.Property(x => x.ReportingDepartmentNameSnapshot).HasMaxLength(200);
        b.Property(x => x.DeviceCodeSnapshot).HasMaxLength(32); b.Property(x => x.DeviceNameSnapshot).HasMaxLength(200);
        b.Property(x => x.DeviceSerialSnapshot).HasMaxLength(100); b.Property(x => x.DeviceDepartmentNameSnapshot).HasMaxLength(200);
        b.HasIndex(x => new { x.DeviceDepartmentIdAtReport, x.DamageReportId });
        b.HasOne<Department>().WithMany().HasForeignKey(x => x.DeviceDepartmentIdAtReport).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Area>().WithMany().HasForeignKey(x => x.DeviceAreaIdAtReport).OnDelete(DeleteBehavior.Restrict);
        ReportMapping.Columns(b);
    }
}
public sealed class DamageReportPendingConfiguration : IEntityTypeConfiguration<DamageReportPendingDevice>
{
    public void Configure(EntityTypeBuilder<DamageReportPendingDevice> b)
    {
        b.ToTable("damage_report_pending_devices"); b.HasKey(x => x.DeviceId); b.Property(x => x.DeviceId).ValueGeneratedNever();
        b.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        ReportMapping.Columns(b);
    }
}
public sealed class DamageReportReceptionConfiguration : IEntityTypeConfiguration<DamageReportReception>
{
    public void Configure(EntityTypeBuilder<DamageReportReception> b)
    {
        b.ToTable("damage_report_receptions"); b.HasKey(x => x.DamageReportId); b.Property(x => x.DamageReportId).ValueGeneratedNever();
        b.HasIndex(x => x.RequestId).IsUnique(); b.HasIndex(x => new { x.ReceivedByAccountId, x.ReceivedAt });
        ReportMapping.Account(b, nameof(DamageReportReception.ReceivedByAccountId)); ReportMapping.Columns(b);
    }
}
public sealed class DamageReportEvidenceConfiguration : IEntityTypeConfiguration<DamageReportEvidence>
{
    public void Configure(EntityTypeBuilder<DamageReportEvidence> b)
    {
        b.ToTable("damage_report_evidence", t =>
        {
            t.HasCheckConstraint("ck_damage_evidence_size_sequence", "size_bytes > 0 AND size_bytes <= 5242880 AND sequence_no > 0");
            t.HasCheckConstraint("ck_damage_evidence_removal", "(removed_at IS NULL) = (removed_by_account_id IS NULL) AND (removed_at IS NULL OR removed_at >= uploaded_at)");
            t.HasCheckConstraint("ck_damage_evidence_type_hash", "content_type IN ('image/png','image/jpeg') AND sha256_hex ~ '^[0-9A-F]{64}$'");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.StorageObjectKey).HasMaxLength(512); b.Property(x => x.OriginalFilename).HasMaxLength(255);
        b.Property(x => x.ContentType).HasMaxLength(128); b.Property(x => x.Sha256Hex).HasMaxLength(64);
        b.HasIndex(x => x.StorageObjectKey).IsUnique(); b.HasIndex(x => new { x.DamageReportId, x.SequenceNo }).IsUnique();
        b.HasIndex(x => new { x.DamageReportId, x.RemovedAt });
        ReportMapping.Account(b, nameof(DamageReportEvidence.UploadedByAccountId)); ReportMapping.Account(b, nameof(DamageReportEvidence.RemovedByAccountId));
        ReportMapping.Columns(b);
        b.Property(x => x.Sha256Hex).HasColumnName("sha256_hex");
    }
}
public sealed class DamageReportHistoryConfiguration : IEntityTypeConfiguration<DamageReportHistory>
{
    public void Configure(EntityTypeBuilder<DamageReportHistory> b)
    {
        b.ToTable("damage_report_history"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ActionCode).HasMaxLength(64); b.Property(x => x.Outcome).HasColumnType("audit_outcome"); b.Property(x => x.ReasonCode).HasMaxLength(80);
        b.Property(x => x.ChangedFields).HasColumnType("jsonb"); b.Property(x => x.BeforeData).HasColumnType("jsonb"); b.Property(x => x.AfterData).HasColumnType("jsonb");
        b.HasIndex(x => new { x.RequestId, x.ActionCode, x.DamageReportId }).IsUnique();
        b.HasIndex(x => new { x.DamageReportId, x.OccurredAt }); b.HasIndex(x => new { x.ActorAccountId, x.OccurredAt });
        b.HasOne<DamageReport>().WithMany().HasForeignKey(x => x.DamageReportId).OnDelete(DeleteBehavior.Restrict);
        ReportMapping.Account(b, nameof(DamageReportHistory.ActorAccountId)); ReportMapping.Columns(b);
    }
}
public sealed class DamageReportRequestConfiguration : IEntityTypeConfiguration<DamageReportRequest>
{
    public void Configure(EntityTypeBuilder<DamageReportRequest> b)
    {
        b.ToTable("damage_report_requests"); b.HasKey(x => x.RequestId); b.Property(x => x.RequestId).ValueGeneratedNever();
        b.Property(x => x.Operation).HasMaxLength(16); b.Property(x => x.PayloadHash).HasMaxLength(64); b.Property(x => x.ResponseJson).HasColumnType("jsonb");
        b.HasOne<DamageReport>().WithMany().HasForeignKey(x => x.DamageReportId).OnDelete(DeleteBehavior.Restrict);
        ReportMapping.Account(b, nameof(DamageReportRequest.ActorAccountId)); ReportMapping.Columns(b);
    }
}
