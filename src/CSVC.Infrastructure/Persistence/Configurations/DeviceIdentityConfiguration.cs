using CSVC.Core.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class DeviceIdentityConfiguration : IEntityTypeConfiguration<DeviceIdentity>
{
    public void Configure(EntityTypeBuilder<DeviceIdentity> builder)
    {
        builder.ToTable("device_identities", table =>
        {
            table.HasCheckConstraint("ck_device_identities_device_code", "btrim(device_code) <> ''");
            table.HasCheckConstraint("ck_device_identities_barcode", "btrim(barcode) <> ''");
        });
        builder.HasKey(x => x.DeviceCode);
        builder.Property(x => x.DeviceCode).HasColumnName("device_code").HasColumnType("varchar(32)").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Barcode).HasColumnName("barcode").HasColumnType("varchar(32)").HasMaxLength(32).IsRequired();
        builder.HasAlternateKey(x => x.Barcode).HasName("uq_device_identities_barcode");
        builder.Property(x => x.ReservedAt).HasColumnName("reserved_at").HasColumnType("timestamptz");
    }
}
