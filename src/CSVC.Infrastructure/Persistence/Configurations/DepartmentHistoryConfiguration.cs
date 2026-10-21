using CSVC.Core.Accounts;
using CSVC.Core.Departments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSVC.Infrastructure.Persistence.Configurations;

public sealed class DepartmentHistoryConfiguration : IEntityTypeConfiguration<DepartmentHistory>
{
    public void Configure(EntityTypeBuilder<DepartmentHistory> builder)
    {
        builder.ToTable("department_history");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        builder.Property(entity => entity.RequestId).HasColumnName("request_id").HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ActorAccountId).HasColumnName("actor_account_id").HasColumnType("uuid");
        builder.Property(entity => entity.ActionCode).HasColumnName("action_code").HasColumnType("varchar(64)").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Outcome).HasColumnName("outcome").HasColumnType("audit_outcome").IsRequired();
        builder.Property(entity => entity.ReasonCode).HasColumnName("reason_code").HasColumnType("varchar(80)").HasMaxLength(80);
        builder.Property(entity => entity.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(entity => entity.DepartmentId).HasColumnName("department_id").HasColumnType("uuid");
        builder.Property(entity => entity.BeforeData).HasColumnName("before_data").HasColumnType("jsonb");
        builder.Property(entity => entity.AfterData).HasColumnName("after_data").HasColumnType("jsonb");
        builder.HasIndex(entity => new { entity.RequestId, entity.ActionCode }).IsUnique().HasDatabaseName("uq_department_history_request_action");
        builder.HasIndex(entity => new { entity.DepartmentId, entity.OccurredAt }).HasDatabaseName("ix_department_history_target");
        builder.HasOne<Account>().WithMany().HasForeignKey(entity => entity.ActorAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(entity => entity.DepartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
