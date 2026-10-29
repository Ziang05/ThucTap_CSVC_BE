using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.Core.DeviceLoans;
using CSVC.Core.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class DeviceLoanConfiguration : IEntityTypeConfiguration<DeviceLoan>
{
    public void Configure(EntityTypeBuilder<DeviceLoan> builder)
    {
        builder.ToTable("device_loans", table => table.HasCheckConstraint("ck_device_loans_due_at", "due_at > created_at"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(x => x.LoanCode).HasColumnName("loan_code").HasColumnType("varchar(32)").HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.LoanCode).IsUnique().HasDatabaseName("uq_device_loans_code");
        builder.Property(x => x.BorrowerAccountId).HasColumnName("borrower_account_id").HasColumnType("uuid");
        builder.Property(x => x.BorrowerDepartmentId).HasColumnName("borrower_department_id").HasColumnType("uuid");
        builder.Property(x => x.Purpose).HasColumnName("purpose").HasColumnType("varchar(1000)").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.DueAt).HasColumnName("due_at").HasColumnType("timestamptz");
        builder.Property(x => x.CreatorAccountId).HasColumnName("creator_account_id").HasColumnType("uuid");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("device_loan_status");
        builder.Property(x => x.DecisionAccountId).HasColumnName("decision_account_id").HasColumnType("uuid");
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at").HasColumnType("timestamptz");
        builder.Property(x => x.HandedOverAt).HasColumnName("handed_over_at").HasColumnType("timestamptz");
        builder.Property(x => x.HandoverAccountId).HasColumnName("handover_account_id").HasColumnType("uuid");
        builder.Property(x => x.ReturnedAt).HasColumnName("returned_at").HasColumnType("timestamptz");
        builder.Property(x => x.ReturnAccountId).HasColumnName("return_account_id").HasColumnType("uuid");
        builder.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasColumnType("varchar(1000)").HasMaxLength(1000);
        builder.Property(x => x.Notes).HasColumnName("notes").HasColumnType("text");
        builder.Property(x => x.Version).HasColumnName("version").HasColumnType("integer").HasDefaultValue(1).IsConcurrencyToken();
        builder.HasIndex(x => new { x.Status, x.DueAt }).HasDatabaseName("ix_device_loans_status_due");
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.BorrowerAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(x => x.BorrowerDepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.CreatorAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.DecisionAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.HandoverAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.ReturnAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.LoanId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeviceLoanItemConfiguration : IEntityTypeConfiguration<DeviceLoanItem>
{
    public void Configure(EntityTypeBuilder<DeviceLoanItem> builder)
    {
        builder.ToTable("device_loan_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(x => x.LoanId).HasColumnName("loan_id").HasColumnType("uuid");
        builder.Property(x => x.DeviceId).HasColumnName("device_id").HasColumnType("uuid");
        builder.Property(x => x.ConditionAtHandover).HasColumnName("condition_at_handover").HasColumnType("device_condition");
        builder.Property(x => x.HandoverNote).HasColumnName("handover_note").HasColumnType("varchar(1000)").HasMaxLength(1000);
        builder.Property(x => x.ConditionAtReturn).HasColumnName("condition_at_return").HasColumnType("device_condition");
        builder.Property(x => x.ReturnNote).HasColumnName("return_note").HasColumnType("varchar(1000)").HasMaxLength(1000);
        builder.HasIndex(x => new { x.LoanId, x.DeviceId }).IsUnique().HasDatabaseName("uq_device_loan_items_loan_device");
        builder.HasIndex(x => x.DeviceId).HasDatabaseName("ix_device_loan_items_device");
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
    }
}
