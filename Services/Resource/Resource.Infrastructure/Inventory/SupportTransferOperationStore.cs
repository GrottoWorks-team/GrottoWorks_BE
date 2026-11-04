using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Application.Inventory;
using Resource.Domain.Inventory;
using Resource.Infrastructure.Data;

namespace Resource.Infrastructure.Inventory;

public sealed class SupportTransferOperationStore(ResourceDbContext db) : ISupportTransferOperationStore
{
    public async Task<T> InOperationTransactionAsync<T>(Guid id, Func<Task<T>> action, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var key = $"support-transfer-operation:{id:D}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))", ct);
        var result = await action();
        await transaction.CommitAsync(ct);
        return result;
    }
    public Task<SupportTransferOperation?> GetAsync(Guid id, CancellationToken ct) =>
        db.Set<SupportTransferOperation>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, ct);
    public void Add(SupportTransferOperation operation) => db.Add(operation);
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class SupportTransferOperationConfiguration : IEntityTypeConfiguration<SupportTransferOperation>
{
    public void Configure(EntityTypeBuilder<SupportTransferOperation> entity)
    {
        entity.ToTable("support_transfer_operation");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("operation_id").ValueGeneratedNever();
        entity.Property(row => row.SupportResponseMaterialId).HasColumnName("support_response_material_id");
        entity.Property(row => row.SourceWorkAreaId).HasColumnName("source_work_area_id");
        entity.Property(row => row.TargetWorkAreaId).HasColumnName("target_work_area_id");
        entity.Property(row => row.MaterialId).HasColumnName("material_id");
        entity.Property(row => row.Quantity).HasColumnName("quantity").HasPrecision(12, 2);
        entity.Property(row => row.ApprovedByUserId).HasColumnName("approved_by_user_id");
        entity.Property(row => row.Status).HasColumnName("status").HasMaxLength(20);
        entity.Property(row => row.ErrorCode).HasColumnName("error_code").HasMaxLength(100);
        entity.Property(row => row.DecidedAt).HasColumnName("decided_at");
        entity.HasIndex(row => row.SupportResponseMaterialId);
    }
}
