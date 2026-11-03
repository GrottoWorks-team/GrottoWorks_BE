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
        var changesPlanning = ChangeTracker.Entries().Any(entry =>
            entry.Entity is Season or WorkArea && entry.State is EntityState.Added or EntityState.Modified);
        if (changesPlanning && Database.IsRelational() && Database.CurrentTransaction is null)
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
            await Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71110305)", cancellationToken);
            var result = await SavePlanningChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        return await SavePlanningChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private async Task<int> SavePlanningChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken)
    {
        await ReconcileSeasonChangesAsync(cancellationToken);
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
                {
                    var recipients = await WorkAreas.Where(area => area.SeasonId == season.Id).ToListAsync(cancellationToken);
                    var payload = PlanningSnapshotMapper.SeasonPayload(snapshot.Season,
                        recipients.Concat(WorkAreas.Local.Where(area => area.SeasonId == season.Id)).DistinctBy(area => area.Id));
                    await StageAsync(DomainEvent(snapshot, eventType, "season", payload), cancellationToken);
                }
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
            var communityChanged = entry.Property(item => item.CommunityId).OriginalValue != area.CommunityId;
            var leaderChanged = entry.Property(item => item.LeaderUserId).OriginalValue != area.LeaderUserId;
            var officerChanged = entry.Property(item => item.MaterialOfficerUserId).OriginalValue != area.MaterialOfficerUserId;
            var eventType = entry.State == EntityState.Added ? "planning.work-area.created"
                : communityChanged || leaderChanged || officerChanged
                    ? "planning.work-area.assigned" : "planning.work-area.updated";
            var payload = eventType == "planning.work-area.assigned"
                ? PlanningSnapshotMapper.AssignmentPayload(snapshot.WorkArea!, communityChanged, leaderChanged, officerChanged)
                : JsonSerializer.SerializeToElement(snapshot.WorkArea!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await StageAsync(DomainEvent(snapshot, eventType, "work_area", payload), cancellationToken);
        }

        // EF commits the business changes and snapshots in the same SaveChanges transaction.
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private async Task ReconcileSeasonChangesAsync(CancellationToken cancellationToken)
    {
        ChangeTracker.DetectChanges();
        var changedSeasons = ChangeTracker.Entries<Season>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified).ToList();
        var changedAreas = ChangeTracker.Entries<WorkArea>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified).ToList();
        var seasonIds = changedSeasons.Select(entry => entry.Entity.Id)
            .Concat(changedAreas.Select(entry => entry.Entity.SeasonId)).Distinct().Order().ToArray();
        foreach (var seasonId in seasonIds)
        {
            var season = Seasons.Local.SingleOrDefault(x => x.Id == seasonId)
                ?? await Seasons.SingleOrDefaultAsync(x => x.Id == seasonId, cancellationToken);
            if (season is null) throw new InvalidOperationException("A Work Area must have an owning Season.");
            var stored = await Seasons.AsNoTracking().SingleOrDefaultAsync(x => x.Id == seasonId, cancellationToken);
            if (stored?.Status == ParishCoordination.Domain.Enums.SeasonStatus.Archived)
                throw new InvalidOperationException("An archived Season is immutable.");
            if (stored?.Status == ParishCoordination.Domain.Enums.SeasonStatus.Completed && changedAreas.Any(x => x.Entity.SeasonId == seasonId))
                throw new InvalidOperationException("Completed Season Work Areas are immutable.");
            var persistedAreas = await WorkAreas.Where(x => x.SeasonId == seasonId).ToListAsync(cancellationToken);
            var areas = persistedAreas.Concat(WorkAreas.Local.Where(x => x.SeasonId == seasonId)).DistinctBy(x => x.Id).ToList();
            var entry = Entry(season);
            if (entry.State == EntityState.Modified && season.Status == ParishCoordination.Domain.Enums.SeasonStatus.Completed &&
                entry.Property(x => x.Status).OriginalValue == ParishCoordination.Domain.Enums.SeasonStatus.Ready)
            {
                // Same transaction/lock as inbound lifecycle events, including every area version and outbox.
                if (stored is null || stored.Version != entry.Property(x => x.Version).OriginalValue ||
                    areas.Count == 0 || areas.Any(x => x.Status != ParishCoordination.Domain.Enums.WorkAreaStatus.Ready))
                    throw new ParishCoordination.Application.Seasons.SeasonConcurrencyException(
                        new InvalidOperationException("Season readiness changed before completion."));
                season.CompleteWorkAreas(areas, season.UpdatedAt ?? DateTimeOffset.UtcNow);
            }
            else season.ReconcileReadiness(areas.Select(x => x.Status).ToArray(), DateTimeOffset.UtcNow);
        }
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
