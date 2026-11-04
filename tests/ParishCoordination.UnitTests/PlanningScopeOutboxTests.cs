using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ParishCoordination.Infrastructure.Data;
using ParishCoordination.Infrastructure.Events;

namespace ParishCoordination.UnitTests;

public sealed class PlanningScopeOutboxTests
{
    [Fact]
    public async Task Seeder_captures_season_and_area_once_and_includes_parent_snapshot()
    {
        await using var db = CreateDatabase();
        var seeder = new ParishCoordinationDataSeeder(db, NullLogger<ParishCoordinationDataSeeder>.Instance);
        await seeder.SeedAsync();
        await seeder.SeedAsync();
        var messages = await db.PlanningEventOutbox.Where(row => row.EventType == "scope.snapshot").ToListAsync();
        messages.Should().HaveCount(2);
        var area = messages.Single(row => row.AggregateId == ParishCoordinationDataSeeder.DemoWorkAreaId);
        var payload = area.ToScopeSnapshot();
        payload.WorkArea!.SeasonId.Should().Be(ParishCoordinationDataSeeder.DemoSeasonId);
        payload.Season.Status.Should().Be("PLANNING");
        payload.WorkArea!.Status.Should().Be("PLANNED");
        payload.WorkArea!.Version.Should().Be(1);
    }

    [Fact]
    public async Task Assignment_and_planning_changes_capture_new_versions_but_no_op_does_not()
    {
        await using var db = CreateDatabase();
        await new ParishCoordinationDataSeeder(db, NullLogger<ParishCoordinationDataSeeder>.Instance).SeedAsync();
        var area = await db.WorkAreas.SingleAsync();
        var leaderId = Guid.NewGuid();
        area.AssignLeader(leaderId, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        area.AssignLeader(leaderId, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        var messages = await db.PlanningEventOutbox.Where(row => row.EventType == "scope.snapshot" && row.AggregateId == area.Id).ToListAsync();
        messages.Select(row => row.SourceVersion).Should().BeEquivalentTo(new long[] { 1, 2 });
        messages.Single(row => row.SourceVersion == 2).ToScopeSnapshot().WorkArea!.LeaderUserId.Should().Be(leaderId);
    }

    [Fact]
    public void Outbox_has_unique_aggregate_version_and_pending_index()
    {
        using var db = new ParishCoordinationDbContext(
            new DbContextOptionsBuilder<ParishCoordinationDbContext>()
                .UseNpgsql("Host=localhost;Database=metadata_only").Options);
        var entity = db.Model.FindEntityType(typeof(PlanningEventOutboxMessage))!;
        entity.GetTableName().Should().Be("planning_event_outbox");
        entity.GetIndexes().Should().Contain(index => index.IsUnique && index.Properties.Count == 3);
        entity.GetIndexes().Should().Contain(index => index.GetFilter() == "published_at IS NULL");
        entity.FindProperty(nameof(PlanningEventOutboxMessage.Envelope))!.GetColumnType().Should().Be("jsonb");
    }

    private static ParishCoordinationDbContext CreateDatabase() => new(
        new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
