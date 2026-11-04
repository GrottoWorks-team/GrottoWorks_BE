using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParishCoordination.Domain.Entities;
using ParishCoordination.Domain.Enums;
using ParishCoordination.Infrastructure.Data;
using ParishCoordination.Infrastructure.Events;

namespace ParishCoordination.UnitTests;

public sealed class PlanningEventTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Role_assignment_event_identifies_only_the_role_changed_in_that_transaction(bool leader)
    {
        using var db = new ParishCoordinationDbContext(new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var season = Season.Create(Guid.NewGuid(), Guid.Empty, "Season", 2026,
            new DateOnly(2026, 10, 1), new DateOnly(2027, 1, 15), null, null, DateTimeOffset.UtcNow);
        db.Seasons.Add(season);
        var originalLeader = Guid.NewGuid();
        var originalOfficer = Guid.NewGuid();
        var area = WorkArea.Create(season.Id, null, null, originalLeader, originalOfficer, "Area", null,
            null, null, WorkAreaStatus.Planned, DateTimeOffset.UtcNow);
        db.WorkAreas.Add(area);
        await db.SaveChangesAsync();
        var newUser = Guid.NewGuid();
        if (leader) area.AssignLeader(newUser, DateTimeOffset.UtcNow);
        else area.AssignMaterialOfficer(newUser, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var message = (await db.PlanningEventOutbox.SingleAsync(row => row.EventType == "planning.work-area.assigned")).ToMessage();
        message.Payload.GetProperty("leaderChanged").GetBoolean().Should().Be(leader);
        message.Payload.GetProperty("materialOfficerChanged").GetBoolean().Should().Be(!leader);
        message.Payload.GetProperty("communityChanged").GetBoolean().Should().BeFalse();
        message.Payload.GetProperty("leaderUserId").GetGuid().Should().Be(leader ? newUser : originalLeader);
        message.Payload.GetProperty("materialOfficerUserId").GetGuid().Should().Be(leader ? originalOfficer : newUser);
        message.AggregateVersion.Should().Be(area.Version);
    }

    [Fact]
    public async Task Community_change_does_not_claim_that_existing_role_holders_were_reassigned()
    {
        using var db = new ParishCoordinationDbContext(new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var season = Season.Create(Guid.NewGuid(), Guid.Empty, "Season", 2026,
            new DateOnly(2026, 10, 1), new DateOnly(2027, 1, 15), null, null, DateTimeOffset.UtcNow);
        db.Seasons.Add(season);
        var area = WorkArea.Create(season.Id, null, null, Guid.NewGuid(), Guid.NewGuid(), "Area", null,
            null, null, WorkAreaStatus.Planned, DateTimeOffset.UtcNow);
        db.WorkAreas.Add(area);
        await db.SaveChangesAsync();
        area.AssignCommunity(Guid.NewGuid(), DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        var message = (await db.PlanningEventOutbox.SingleAsync(row => row.EventType == "planning.work-area.assigned")).ToMessage();
        message.Payload.GetProperty("communityChanged").GetBoolean().Should().BeTrue();
        message.Payload.GetProperty("leaderChanged").GetBoolean().Should().BeFalse();
        message.Payload.GetProperty("materialOfficerChanged").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Snapshot_contains_ownership_coordinates_and_parent_version()
    {
        var season = Season.Create(Guid.NewGuid(), Guid.Empty, "Season", 2026,
            new DateOnly(2026, 10, 1), new DateOnly(2027, 1, 15), null, null, DateTimeOffset.UtcNow);
        var area = WorkArea.Create(season.Id, null, null, Guid.NewGuid(), Guid.NewGuid(), "Area", null,
            null, null, WorkAreaStatus.Planned, DateTimeOffset.UtcNow, 10.1234567m, 106.1234567m);
        var snapshot = PlanningSnapshotMapper.Scope(season, area);
        var row = PlanningEventOutboxMessage.Create(snapshot);
        var recovered = row.ToScopeSnapshot();
        recovered.WorkArea!.Latitude.Should().Be(area.Latitude);
        recovered.WorkArea.MaterialOfficerUserId.Should().Be(area.MaterialOfficerUserId);
        recovered.Season.Version.Should().Be(season.Version);
        row.IsScopeSnapshot.Should().BeTrue();
        row.PublishedAt.Should().BeNull();
    }

    [Fact]
    public async Task Saving_work_area_records_scope_and_business_event_once()
    {
        using var db = new ParishCoordinationDbContext(new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var season = Season.Create(Guid.NewGuid(), Guid.Empty, "Season", 2026,
            new DateOnly(2026, 10, 1), new DateOnly(2027, 1, 15), null, null, DateTimeOffset.UtcNow);
        db.Seasons.Add(season);
        await db.SaveChangesAsync();
        var area = WorkArea.Create(season.Id, null, null, null, null, "Area", null, null, null,
            WorkAreaStatus.Planned, DateTimeOffset.UtcNow);
        db.WorkAreas.Add(area);
        await db.SaveChangesAsync();
        var count = await db.PlanningEventOutbox.CountAsync();
        await db.SaveChangesAsync();
        (await db.PlanningEventOutbox.CountAsync()).Should().Be(count);
        db.PlanningEventOutbox.Should().Contain(row => row.AggregateId == area.Id && row.EventType == "planning.work-area.created");
    }
}
