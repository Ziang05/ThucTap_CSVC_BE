using CSVC.Core.Accounts;
using CSVC.Core.ProcurementRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class ProcurementRequestConfiguration : IEntityTypeConfiguration<ProcurementRequest>
{
    public void Configure(EntityTypeBuilder<ProcurementRequest> b)
    {
        b.ToTable("procurement_requests", t => t.HasCheckConstraint("ck_procurement_versions", "version > 0 AND source_version > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.DisplayCode).HasMaxLength(32).IsRequired(); b.HasIndex(x => x.DisplayCode).IsUnique();
        b.Property(x => x.Source).HasMaxLength(32).IsRequired(); b.Property(x => x.ExternalCode).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.Source, x.ExternalCode }).IsUnique();
        b.Property(x => x.RequestType).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired(); b.Property(x => x.RequestingDepartmentCode).HasMaxLength(32).IsRequired();
        b.Property(x => x.Contact).HasMaxLength(200); b.Property(x => x.ContractReference).HasMaxLength(100); b.Property(x => x.SupplierReference).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(4000); b.Property(x => x.SourceStatus).HasMaxLength(64);
        b.Property(x => x.PendingSourcePayload).HasColumnType("jsonb"); b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.WorkflowVersion).HasDefaultValue(1);
        b.Property(x => x.ServiceEvidenceJson).HasColumnType("jsonb");
        b.Property(x => x.AcceptanceDecision).HasMaxLength(16); b.Property(x => x.AcceptanceReason).HasMaxLength(1000);
        b.Property(x => x.AcceptanceScopeHash).HasMaxLength(64);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.ServiceRecordedByAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AcceptedByAccountId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.MappingIssue).HasMaxLength(1000); b.Property(x => x.Result).HasMaxLength(32); b.Property(x => x.ResultReason).HasMaxLength(1000);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Status, x.ReceivedAt });
        ProcurementColumnNames.Apply(b.Metadata);
    }
}
public sealed class ProcurementRequestLineConfiguration : IEntityTypeConfiguration<ProcurementRequestLine>
{
    public void Configure(EntityTypeBuilder<ProcurementRequestLine> b)
    {
        b.ToTable("procurement_request_lines", t => t.HasCheckConstraint("ck_procurement_line_quantity", "quantity > 0")); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.SourceLineCode).HasMaxLength(64).IsRequired(); b.HasIndex(x => new { x.RequestId, x.SourceLineCode }).IsUnique();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16); b.Property(x => x.Specialization).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Description).HasMaxLength(1000).IsRequired(); b.Property(x => x.Quantity).HasPrecision(18, 3); b.Property(x => x.Unit).HasMaxLength(32).IsRequired();
        b.Property(x => x.CategoryCode).HasMaxLength(64); b.Property(x => x.DeviceCode).HasMaxLength(32); b.Property(x => x.DamageReportCode).HasMaxLength(64);
        ProcurementColumnNames.Apply(b.Metadata);
    }
}
public sealed class ProcurementRequestEventConfiguration : IEntityTypeConfiguration<ProcurementRequestEvent>
{
    public void Configure(EntityTypeBuilder<ProcurementRequestEvent> b)
    {
        b.ToTable("procurement_request_events"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => x.ExternalEventId).IsUnique().HasFilter("external_event_id IS NOT NULL");
        b.HasIndex(x => x.CommandId).IsUnique().HasFilter("command_id IS NOT NULL");
        b.HasIndex(x => new { x.RequestId, x.SourceVersion }).IsUnique().HasFilter("source_version IS NOT NULL");
        b.Property(x => x.Action).HasMaxLength(64).IsRequired(); b.Property(x => x.Outcome).HasMaxLength(64).IsRequired(); b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.PayloadHash).HasMaxLength(64); b.Property(x => x.Payload).HasColumnType("jsonb"); b.Property(x => x.Response).HasColumnType("jsonb");
        b.HasOne<ProcurementRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorAccountId).OnDelete(DeleteBehavior.Restrict);
        ProcurementColumnNames.Apply(b.Metadata);
    }
}
public sealed class ProcurementDocumentLinkConfiguration : IEntityTypeConfiguration<ProcurementDocumentLink>
{
    public void Configure(EntityTypeBuilder<ProcurementDocumentLink> b)
    {
        b.ToTable("procurement_document_links"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16); b.Property(x => x.DocumentCode).HasMaxLength(64).IsRequired(); b.Property(x => x.BindingsJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.Kind, x.DocumentCode }).IsUnique(); b.HasIndex(x => x.RequestId);
        b.HasOne<ProcurementRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.LinkedByAccountId).OnDelete(DeleteBehavior.Restrict);
        ProcurementColumnNames.Apply(b.Metadata);
    }
}

internal static class ProcurementColumnNames
{
    public static void Apply(Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entity)
    {
        foreach (var property in entity.GetProperties())
            property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }
}
