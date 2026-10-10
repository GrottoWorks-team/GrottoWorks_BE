using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ParishCoordination.Infrastructure.Data;

namespace ParishCoordination.UnitTests;

public sealed class SeederTests
{
    [Fact]
    public async Task Seeder_is_idempotent_and_uses_stable_demo_ids()
    {
        var options = new DbContextOptionsBuilder<ParishCoordinationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new ParishCoordinationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var seeder = new ParishCoordinationDataSeeder(
            dbContext,
            NullLogger<ParishCoordinationDataSeeder>.Instance);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        dbContext.Parishes.Should().ContainSingle(parish =>
            parish.Id == ParishCoordinationDataSeeder.DemoParishId &&
            parish.Name == "GrottoWorks Demo Parish");
        dbContext.Communities.Should().HaveCount(2);
        dbContext.Communities.Select(community => community.Id)
            .Should().BeEquivalentTo(new[]
            {
                ParishCoordinationDataSeeder.DemoCommunityNorthId,
                ParishCoordinationDataSeeder.DemoCommunitySouthId
            });
        dbContext.Communities.Should().OnlyContain(community =>
            community.ParishId == ParishCoordinationDataSeeder.DemoParishId);
    }
}