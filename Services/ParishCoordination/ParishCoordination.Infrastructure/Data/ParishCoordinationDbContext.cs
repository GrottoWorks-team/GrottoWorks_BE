using Microsoft.EntityFrameworkCore;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Infrastructure.TaskScopeSync;

namespace ParishCoordination.Infrastructure.Data;

public sealed class ParishCoordinationDbContext(DbContextOptions<ParishCoordinationDbContext> options)
    : DbContext(options)
{
    public DbSet<Parish> Parishes => Set<Parish>();

    public DbSet<Community> Communities => Set<Community>();

    public DbSet<Season> Seasons => Set<Season>();

    public DbSet<WorkArea> WorkAreas => Set<WorkArea>();

    public DbSet<WorkAreaCategory> WorkAreaCategories => Set<WorkAreaCategory>();

    public DbSet<TaskScopeOutbox> TaskScopeOutbox => Set<TaskScopeOutbox>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(true, cancellationToken);

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        var seasons = ChangeTracker.Entries<Season>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(entry => entry.Entity).ToList();
        var areas = ChangeTracker.Entries<WorkArea>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(entry => entry.Entity).ToList();

        foreach (var season in seasons)
        {
            await EnqueueAsync(TaskScopeSync.TaskScopeOutbox.ForSeason(season), cancellationToken);
        }

        foreach (var area in areas)
        {
            var season = Seasons.Local.SingleOrDefault(item => item.Id == area.SeasonId)
                ?? await Seasons.AsNoTracking().SingleAsync(item => item.Id == area.SeasonId, cancellationToken);
            await EnqueueAsync(TaskScopeSync.TaskScopeOutbox.ForWorkArea(area, season), cancellationToken);
        }

        // EF commits the business changes and snapshots in the same SaveChanges transaction.
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private async Task EnqueueAsync(TaskScopeOutbox message, CancellationToken cancellationToken)
    {
        var tracked = TaskScopeOutbox.Local.Any(row => row.ScopeType == message.ScopeType &&
            row.AggregateId == message.AggregateId && row.SourceVersion == message.SourceVersion);
        if (!tracked && !await TaskScopeOutbox.AnyAsync(row => row.ScopeType == message.ScopeType &&
                row.AggregateId == message.AggregateId && row.SourceVersion == message.SourceVersion, cancellationToken))
        {
            TaskScopeOutbox.Add(message);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ParishCoordinationDbContext).Assembly);
    }
}
