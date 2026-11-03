using Microsoft.EntityFrameworkCore;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Infrastructure.TaskScopeSync;
using ParishCoordination.Infrastructure.Events;
using ParishCoordination.Contracts.Events;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace ParishCoordination.Infrastructure.Data;

public sealed class ParishCoordinationDbContext(DbContextOptions<ParishCoordinationDbContext> options,
    IHttpContextAccessor? httpContextAccessor = null)
    : DbContext(options)
{
    public DbSet<Parish> Parishes => Set<Parish>();

    public DbSet<Community> Communities => Set<Community>();

    public DbSet<Season> Seasons => Set<Season>();

    public DbSet<WorkArea> WorkAreas => Set<WorkArea>();

    public DbSet<WorkAreaCategory> WorkAreaCategories => Set<WorkAreaCategory>();

    public DbSet<TaskScopeOutbox> TaskScopeOutbox => Set<TaskScopeOutbox>();
    public DbSet<PlanningEventOutboxMessage> PlanningEventOutbox => Set<PlanningEventOutboxMessage>();

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
            var snapshot = PlanningSnapshotMapper.Scope(season);
            await StageAsync(PlanningEventOutboxMessage.Create(snapshot), cancellationToken);
            var entry = Entry(season);
            if (entry.State == EntityState.Modified && entry.Property(item => item.Status).IsModified)
            {
                var eventType = season.Status switch
                {
                    ParishCoordination.Domain.Enums.SeasonStatus.Active => "planning.season.activated",
                    ParishCoordination.Domain.Enums.SeasonStatus.Completed => "planning.season.completed",
                    ParishCoordination.Domain.Enums.SeasonStatus.Archived => "planning.season.archived",
                    _ => null
                };
                if (eventType is not null)
                    await StageAsync(DomainEvent(snapshot, eventType, "season", snapshot.Season), cancellationToken);
            }
        }

        foreach (var area in areas)
        {
            var season = Seasons.Local.SingleOrDefault(item => item.Id == area.SeasonId)
                ?? await Seasons.AsNoTracking().SingleAsync(item => item.Id == area.SeasonId, cancellationToken);
            await EnqueueAsync(TaskScopeSync.TaskScopeOutbox.ForWorkArea(area, season), cancellationToken);
            var snapshot = PlanningSnapshotMapper.Scope(season, area);
            await StageAsync(PlanningEventOutboxMessage.Create(snapshot), cancellationToken);
            var entry = Entry(area);
            var eventType = entry.State == EntityState.Added ? "planning.work-area.created"
                : entry.Property(item => item.CommunityId).IsModified ||
                  entry.Property(item => item.LeaderUserId).IsModified ||
                  entry.Property(item => item.MaterialOfficerUserId).IsModified
                    ? "planning.work-area.assigned" : "planning.work-area.updated";
            await StageAsync(DomainEvent(snapshot, eventType, "work_area", snapshot.WorkArea!), cancellationToken);
        }

        // EF commits the business changes and snapshots in the same SaveChanges transaction.
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private PlanningEventOutboxMessage DomainEvent<T>(PlanningScopeSnapshot snapshot, string eventType,
        string aggregateType, T payload)
    {
        var http = httpContextAccessor?.HttpContext;
        Guid? actor = Guid.TryParse(http?.User.FindFirst("sub")?.Value, out var userId) ? userId : null;
        var correlationId = http?.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString();
        return PlanningEventOutboxMessage.Create(new PlanningIntegrationEvent(Guid.NewGuid(), eventType, 1,
            snapshot.CapturedAt, correlationId, actor, snapshot.Season.ParishId, snapshot.Season.SeasonId,
            aggregateType, snapshot.AggregateId, snapshot.SourceVersion,
            JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    }

    private async Task StageAsync(PlanningEventOutboxMessage message, CancellationToken cancellationToken)
    {
        if (PlanningEventOutbox.Local.Any(row => row.AggregateId == message.AggregateId &&
            row.SourceVersion == message.SourceVersion && row.EventType == message.EventType)) return;
        if (await PlanningEventOutbox.AnyAsync(row => row.AggregateId == message.AggregateId &&
            row.SourceVersion == message.SourceVersion && row.EventType == message.EventType, cancellationToken)) return;
        PlanningEventOutbox.Add(message);
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
