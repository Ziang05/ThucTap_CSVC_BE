using CSVC.Core.Accounts;
using CSVC.Core.Areas;
using CSVC.Core.Departments;
using CSVC.Core.Devices;
using CSVC.Core.Materials;
using CSVC.Core.ProcurementRequests;
using CSVC.Core.ServiceOrders;
using CSVC.Core.Suppliers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class MaterialInventoryConfiguration : IEntityTypeConfiguration<MaterialUnit>, IEntityTypeConfiguration<MaterialItem>,
    IEntityTypeConfiguration<MaterialWarehouse>, IEntityTypeConfiguration<MaterialLocation>, IEntityTypeConfiguration<MaterialBalance>,
    IEntityTypeConfiguration<MaterialDocument>, IEntityTypeConfiguration<MaterialDocumentLine>, IEntityTypeConfiguration<MaterialLedgerEntry>,
    IEntityTypeConfiguration<MaterialDocumentHistory>, IEntityTypeConfiguration<MaterialCatalogHistory>, IEntityTypeConfiguration<EquipmentChange>,
    IEntityTypeConfiguration<EquipmentMaterialUse>, IEntityTypeConfiguration<EquipmentRemovedMaterial>, IEntityTypeConfiguration<MaterialWorkUse>
{
    private static void Base<T>(EntityTypeBuilder<T> b, string table) where T : class
    { b.ToTable(table); b.HasKey("Id"); b.Property<Guid>("Id").ValueGeneratedNever(); }
    private static void Named<T>(EntityTypeBuilder<T> b) where T : class
    { b.Property<string>("Code").HasMaxLength(32); b.HasIndex("Code").IsUnique(); b.Property<string>("Name").HasMaxLength(200); b.Property<int>("Version").IsConcurrencyToken(); }
    private static void Account<T>(EntityTypeBuilder<T> b, string property) where T : class => b.HasOne<Account>().WithMany().HasForeignKey(property).OnDelete(DeleteBehavior.Restrict);
    private static void Finish<T>(EntityTypeBuilder<T> b) where T : class => ProcurementColumnNames.Apply(b.Metadata);
    public void Configure(EntityTypeBuilder<MaterialUnit> b)
    {
        Base(b, "material_units"); Named(b); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.ToTable(t => t.HasCheckConstraint("ck_material_unit_scale", "decimal_places BETWEEN 0 AND 3 AND version > 0")); Finish(b);
    }
    public void Configure(EntityTypeBuilder<MaterialItem> b)
    {
        Base(b, "materials"); Named(b); b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Notes).HasMaxLength(4000); b.HasOne<MaterialUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict); Finish(b);
    }
    public void Configure(EntityTypeBuilder<MaterialWarehouse> b)
    {
        Base(b, "material_warehouses"); Named(b); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16); b.Property(x => x.Notes).HasMaxLength(4000);
        b.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict); Finish(b);
    }
    public void Configure(EntityTypeBuilder<MaterialLocation> b)
    {
        Base(b, "material_locations"); Named(b); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.HasOne<MaterialWarehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Area>().WithMany().HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict); Finish(b);
    }
    public void Configure(EntityTypeBuilder<MaterialBalance> b)
    {
        Base(b, "material_balances"); b.Property(x => x.Quantity).HasPrecision(18, 3); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.MaterialId, x.LocationId }).IsUnique();
        b.HasOne<MaterialItem>().WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MaterialLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("ck_material_balance_nonnegative", "quantity >= 0 AND version > 0")); Finish(b);
    }
    public void Configure(EntityTypeBuilder<MaterialDocument> b)
    {
        Base(b, "material_documents"); b.Property(x => x.Code).HasMaxLength(32); b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Version).IsConcurrencyToken(); b.Property(x => x.CreateHash).HasMaxLength(64);
        b.Property(x => x.Reference).HasMaxLength(200); b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.Notes).HasMaxLength(4000);
        b.HasIndex(x => x.CreateRequestId).IsUnique(); b.HasIndex(x => new { x.DepartmentId, x.Status, x.CreatedAt });
        b.HasIndex(x => new { x.ProcurementRequestId, x.Kind });
        foreach (var field in new[] { nameof(MaterialDocument.CreatedByAccountId), nameof(MaterialDocument.ApprovedByAccountId), nameof(MaterialDocument.PostedByAccountId), nameof(MaterialDocument.ReceivedByAccountId), nameof(MaterialDocument.TargetResponsibleAccountId) }) Account(b, field);
        b.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Department>().WithMany().HasForeignKey(x => x.TargetDepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ServiceOrder>().WithMany().HasForeignKey(x => x.ServiceOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProcurementRequest>().WithMany().HasForeignKey(x => x.ProcurementRequestId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("ck_material_document_posting", "version > 0 AND ((posted_at IS NULL AND posted_by_account_id IS NULL) OR (posted_at IS NOT NULL AND posted_by_account_id IS NOT NULL))")); Finish(b);
    }
    public void Configure(EntityTypeBuilder<MaterialDocumentLine> b)
    {
        Base(b, "material_document_lines"); b.HasAlternateKey(x => new { x.Id, x.DocumentId }); b.HasIndex(x => new { x.DocumentId, x.LineNo }).IsUnique();
        b.Property(x => x.Quantity).HasPrecision(18, 3); b.Property(x => x.Condition).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.ConditionNote).HasMaxLength(1000); b.Property(x => x.SourceLineCode).HasMaxLength(64); b.Property(x => x.LotOrSerial).HasMaxLength(100);
        b.Property(x => x.MaterialCodeSnapshot).HasMaxLength(32); b.Property(x => x.MaterialNameSnapshot).HasMaxLength(200);
        b.Property(x => x.UnitCodeSnapshot).HasMaxLength(32); b.Property(x => x.UnitNameSnapshot).HasMaxLength(200);
        b.HasOne<MaterialItem>().WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MaterialLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MaterialLocation>().WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MaterialDocumentLine>().WithMany().HasForeignKey(x => x.OriginalIssueLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EquipmentRemovedMaterial>().WithMany().HasForeignKey(x => x.RemovedMaterialId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("ck_material_line_quantity", "quantity <> 0 AND line_no > 0")); Finish(b);
    }
    public void Configure(EntityTypeBuilder<MaterialLedgerEntry> b)
    {
        Base(b, "material_ledger"); b.Property(x => x.Delta).HasPrecision(18, 3); b.Property(x => x.BalanceAfter).HasPrecision(18, 3);
        b.HasIndex(x => new { x.LineId, x.Leg }).IsUnique(); b.HasIndex(x => new { x.MaterialId, x.LocationId, x.OccurredAt });
        b.HasOne<MaterialDocumentLine>().WithMany().HasForeignKey(x => new { x.LineId, x.DocumentId }).HasPrincipalKey(x => new { x.Id, x.DocumentId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MaterialItem>().WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MaterialLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict); Account(b, nameof(MaterialLedgerEntry.ActorAccountId));
        b.ToTable(t => t.HasCheckConstraint("ck_material_ledger_values", "delta <> 0 AND balance_after >= 0 AND leg IN (0,1)")); Finish(b);
    }
    public void Configure(EntityTypeBuilder<MaterialDocumentHistory> b)
    {
        Base(b, "material_document_history"); b.Property(x => x.Action).HasMaxLength(64); b.Property(x => x.Fingerprint).HasMaxLength(64); b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.BeforeData).HasColumnType("jsonb"); b.Property(x => x.AfterData).HasColumnType("jsonb");
        b.HasIndex(x => x.RequestId).IsUnique().HasFilter("request_id IS NOT NULL"); b.HasIndex(x => new { x.DocumentId, x.OccurredAt });
        b.HasOne<MaterialDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict); Account(b, nameof(MaterialDocumentHistory.ActorAccountId)); Finish(b);
    }
    public void Configure(EntityTypeBuilder<MaterialCatalogHistory> b)
    {
        Base(b, "material_catalog_history"); b.Property(x => x.Catalog).HasMaxLength(32); b.Property(x => x.Code).HasMaxLength(64); b.Property(x => x.Action).HasMaxLength(64);
        b.Property(x => x.BeforeData).HasColumnType("jsonb"); b.Property(x => x.AfterData).HasColumnType("jsonb"); b.HasIndex(x => new { x.Catalog, x.TargetId, x.OccurredAt }); Account(b, nameof(MaterialCatalogHistory.ActorAccountId)); Finish(b);
    }
    public void Configure(EntityTypeBuilder<EquipmentChange> b)
    {
        Base(b, "equipment_changes"); b.Property(x => x.Code).HasMaxLength(32); b.HasIndex(x => x.Code).IsUnique(); b.HasIndex(x => x.RequestId).IsUnique();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16); b.Property(x => x.Fingerprint).HasMaxLength(64);
        b.Property(x => x.Target).HasMaxLength(200); b.Property(x => x.BeforeValue).HasMaxLength(4000); b.Property(x => x.AfterValue).HasMaxLength(4000);
        b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.Executor).HasMaxLength(200);
        b.HasIndex(x => new { x.DeviceId, x.PerformedAt }); b.HasIndex(x => x.ReplacesId).IsUnique().HasFilter("replaces_id IS NOT NULL");
        b.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ServiceOrder>().WithMany().HasForeignKey(x => x.ServiceOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EquipmentChange>().WithMany().HasForeignKey(x => x.ReplacesId).OnDelete(DeleteBehavior.Restrict); Account(b, nameof(EquipmentChange.RecordedByAccountId));
        b.HasMany(x => x.Uses).WithOne().HasForeignKey(x => x.ChangeId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Removed).WithOne().HasForeignKey(x => x.ChangeId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("ck_equipment_change_time", "performed_at <= recorded_at AND (before_value IS NOT NULL OR after_value IS NOT NULL)")); Finish(b);
    }
    public void Configure(EntityTypeBuilder<EquipmentMaterialUse> b)
    {
        Base(b, "equipment_material_uses"); b.Property(x => x.Source).HasConversion<string>().HasMaxLength(16); b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.Description).HasMaxLength(1000); b.Property(x => x.Unit).HasMaxLength(200); b.Property(x => x.LotOrSerial).HasMaxLength(100); b.Property(x => x.SourceReference).HasMaxLength(1000);
        b.HasOne<MaterialItem>().WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MaterialDocumentLine>().WithMany().HasForeignKey(x => x.IssueLineId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("ck_equipment_use_source", "quantity > 0 AND ((source = 'Warehouse' AND issue_line_id IS NOT NULL) OR (source <> 'Warehouse' AND issue_line_id IS NULL))")); Finish(b);
    }
    public void Configure(EntityTypeBuilder<EquipmentRemovedMaterial> b)
    {
        Base(b, "equipment_removed_materials"); b.Property(x => x.Quantity).HasPrecision(18, 3); b.Property(x => x.Disposition).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Description).HasMaxLength(1000); b.Property(x => x.Unit).HasMaxLength(200); b.Property(x => x.ConditionNote).HasMaxLength(1000);
        b.Property(x => x.RecipientOrEvidence).HasMaxLength(1000); b.Property(x => x.LotOrSerial).HasMaxLength(100);
        b.HasOne<MaterialItem>().WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("ck_equipment_removed_return", "quantity > 0 AND (disposition <> 'ReturnToStock' OR (reusable AND material_id IS NOT NULL))")); Finish(b);
    }
    public void Configure(EntityTypeBuilder<MaterialWorkUse> b)
    {
        Base(b, "material_work_uses"); b.Property(x => x.Code).HasMaxLength(32); b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.RequestId).IsUnique(); b.HasIndex(x => x.ReplacesId).IsUnique().HasFilter("replaces_id IS NOT NULL");
        b.Property(x => x.Fingerprint).HasMaxLength(64); b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.HasOne<MaterialDocumentLine>().WithMany().HasForeignKey(x => x.IssueLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MaterialWorkUse>().WithMany().HasForeignKey(x => x.ReplacesId).OnDelete(DeleteBehavior.Restrict); Account(b, nameof(MaterialWorkUse.ActorAccountId));
        b.ToTable(t => t.HasCheckConstraint("ck_material_work_use_qty", "quantity > 0")); Finish(b);
    }
}
