using CSVC.Core.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class DeviceOperationHoldConfiguration : IEntityTypeConfiguration<DeviceOperationHold>
{
    public void Configure(EntityTypeBuilder<DeviceOperationHold> builder)
    {
        builder.ToTable("device_operation_holds");
        builder.HasKey(x => x.DeviceCode);
        builder.Property(x => x.DeviceCode).HasColumnName("device_code").HasColumnType("varchar(32)").HasMaxLength(32);
        builder.Property(x => x.DeviceId).HasColumnName("device_id").HasColumnType("uuid");
        builder.Property(x => x.OperationKind).HasColumnName("operation_kind").HasColumnType("varchar(16)").HasMaxLength(16).HasConversion<string>();
        builder.Property(x => x.OperationId).HasColumnName("operation_id").HasColumnType("uuid");
        builder.Property(x => x.HeldAt).HasColumnName("held_at").HasColumnType("timestamptz");
        builder.HasIndex(x => x.DeviceId).IsUnique().HasFilter("device_id IS NOT NULL").HasDatabaseName("uq_device_operation_holds_device_id");
        builder.HasIndex(x => new { x.OperationKind, x.OperationId }).HasDatabaseName("ix_device_operation_holds_operation");
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
    }
}
