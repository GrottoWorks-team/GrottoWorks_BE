using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParishCoordination.Application.Parishes;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.Infrastructure.Files;

internal sealed class ParishMapAttachmentStore(ParishCoordinationDbContext db) : IParishMapAttachmentStore
{
    public async Task<IAsyncDisposable> LockAsync(Guid parishId, CancellationToken ct)
    {
        // A session lock covers the journal and HTTP calls without holding a database transaction open.
        var key = parishId.ToString("D");
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock(hashtextextended({key}, 845052));", ct);
            return new Lease(db, key);
        }
        catch
        {
            await db.Database.CloseConnectionAsync();
            throw;
        }
    }

    public Task<ParishMapAttachmentIntent?> FindAsync(Guid parishId, Guid fileId, CancellationToken ct) =>
        db.Set<ParishMapAttachmentIntent>().SingleOrDefaultAsync(
            row => row.ParishId == parishId && row.FileId == fileId, ct);

    public void Add(ParishMapAttachmentIntent intent) => db.Add(intent);

    public async Task RemoveAsync(ParishMapAttachmentIntent intent, CancellationToken ct)
    {
        db.Remove(intent);
        await db.SaveChangesAsync(ct);
    }

    private sealed class Lease(ParishCoordinationDbContext db, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_unlock(hashtextextended({key}, 845052));", CancellationToken.None);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}

internal sealed class ParishMapAttachmentConfiguration : IEntityTypeConfiguration<ParishMapAttachmentIntent>
{
    public void Configure(EntityTypeBuilder<ParishMapAttachmentIntent> builder)
    {
        builder.ToTable("parish_map_attachment_intent");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever().HasColumnName("reservation_id");
        builder.Property(row => row.ParishId).HasColumnName("parish_id");
        builder.Property(row => row.FileId).HasColumnName("file_id");
        builder.Property(row => row.MapSaved).HasColumnName("map_saved");
        builder.Property(row => row.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.HasIndex(row => new { row.ParishId, row.FileId }).IsUnique();
        builder.HasIndex(row => row.NextAttemptAt);
    }
}
