using Microsoft.EntityFrameworkCore;
using Readiness.Domain.Checklists;
using Readiness.Domain.Scope;

namespace Readiness.Infrastructure.Data;

/// <summary>readiness_db — tables owned by the Readiness service (spec section 3, ERD v3.6.2).</summary>
public sealed class ReadinessDbContext(DbContextOptions<ReadinessDbContext> options) : DbContext(options)
{
    public DbSet<WorkAreaScope> WorkAreaScopes => Set<WorkAreaScope>();

    public DbSet<TaskProjection> TaskProjections => Set<TaskProjection>();

    public DbSet<ReadinessChecklist> Checklists => Set<ReadinessChecklist>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReadinessDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        TouchChangedChecklists();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// An item change alone does not update the checklist row; marking the checklist modified bumps its <c>xmin</c>, so the
    /// checklist version guards every item edit — including RECHECK set by a completed corrective task.
    /// </summary>
    private void TouchChangedChecklists()
    {
        ChangeTracker.DetectChanges();
        var changed = ChangeTracker.Entries<ReadinessItem>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Entity.ReadinessChecklistId)
            .ToHashSet();
        foreach (var entry in ChangeTracker.Entries<ReadinessChecklist>()
                     .Where(entry => entry.State == EntityState.Unchanged && changed.Contains(entry.Entity.ReadinessChecklistId)))
        {
            entry.State = EntityState.Modified;
        }
    }
}
