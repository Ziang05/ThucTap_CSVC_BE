using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.Core.DeviceLoans;
using CSVC.Core.DeviceReceipts;
using CSVC.Core.DeviceTransfers;
using CSVC.Core.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class DeviceHistoryConfiguration : IEntityTypeConfiguration<DeviceHistory>
{
    public void Configure(EntityTypeBuilder<DeviceHistory> builder)
    {
        ConfigureAudit(builder, "device_history", "device_id");
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
    }

    internal static void ConfigureAudit<TEntity>(EntityTypeBuilder<TEntity> builder, string table, string targetColumn)
        where TEntity : class
    {
        builder.ToTable(table);
        builder.HasKey("Id");
        builder.Property<Guid>("Id").HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property<Guid>("RequestId").HasColumnName("request_id").HasColumnType("uuid").IsRequired();
        builder.Property<Guid?>("ActorAccountId").HasColumnName("actor_account_id").HasColumnType("uuid");
        builder.Property<string>("ActionCode").HasColumnName("action_code").HasColumnType("varchar(64)").HasMaxLength(64).IsRequired();
        builder.Property<AuditOutcome>("Outcome").HasColumnName("outcome").HasColumnType("audit_outcome").IsRequired();
        builder.Property<string>("ReasonCode").HasColumnName("reason_code").HasColumnType("varchar(80)").HasMaxLength(80);
        builder.Property<DateTimeOffset>("OccurredAt").HasColumnName("occurred_at").HasColumnType("timestamptz").IsRequired();
        builder.Property<Guid?>(targetColumn == "device_id" ? "DeviceId" : targetColumn == "receipt_id" ? "ReceiptId" : targetColumn == "loan_id" ? "LoanId" : "TransferId")
            .HasColumnName(targetColumn).HasColumnType("uuid");
        builder.Property<string>("ChangedFields").HasColumnName("changed_fields").HasColumnType("jsonb");
        builder.Property<string>("BeforeData").HasColumnName("before_data").HasColumnType("jsonb");
        builder.Property<string>("AfterData").HasColumnName("after_data").HasColumnType("jsonb");
        builder.HasIndex("RequestId", "ActionCode").IsUnique().HasDatabaseName($"uq_{table}_request_action");
        builder.HasIndex(targetColumn == "device_id" ? "DeviceId" : targetColumn == "receipt_id" ? "ReceiptId" : targetColumn == "loan_id" ? "LoanId" : "TransferId", "OccurredAt")
            .HasDatabaseName($"ix_{table}_target");
        builder.HasIndex("OccurredAt").HasDatabaseName($"ix_{table}_occurred_at");
        builder.HasOne<Account>().WithMany().HasForeignKey("ActorAccountId").OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeviceReceiptHistoryConfiguration : IEntityTypeConfiguration<DeviceReceiptHistory>
{
    public void Configure(EntityTypeBuilder<DeviceReceiptHistory> builder)
    {
        DeviceHistoryConfiguration.ConfigureAudit(builder, "device_receipt_history", "receipt_id");
        builder.HasOne<DeviceReceipt>().WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeviceLoanHistoryConfiguration : IEntityTypeConfiguration<DeviceLoanHistory>
{
    public void Configure(EntityTypeBuilder<DeviceLoanHistory> builder)
    {
        DeviceHistoryConfiguration.ConfigureAudit(builder, "device_loan_history", "loan_id");
        builder.HasOne<DeviceLoan>().WithMany().HasForeignKey(x => x.LoanId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DeviceTransferHistoryConfiguration : IEntityTypeConfiguration<DeviceTransferHistory>
{
    public void Configure(EntityTypeBuilder<DeviceTransferHistory> builder)
    {
        DeviceHistoryConfiguration.ConfigureAudit(builder, "device_transfer_history", "transfer_id");
        builder.HasOne<DeviceTransfer>().WithMany().HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Restrict);
    }
}
