using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ParishCoordination.Infrastructure.Data;
using ParishCoordination.Infrastructure.Repositories;

namespace ParishCoordination.UnitTests;

public sealed class NotificationAudienceTests
{
    [Fact]
    public async Task Audience_is_complete_and_rejects_an_unrelated_parish()
    {
        await using var db = new ParishCoordinationDbContext(
            new DbContextOptionsBuilder<ParishCoordinationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await new ParishCoordinationDataSeeder(db, NullLogger<ParishCoordinationDataSeeder>.Instance).SeedAsync();
        var query = new NotificationAudienceQuery(db);
        var audience = await query.GetAsync(ParishCoordinationDataSeeder.DemoParishId,
            ParishCoordinationDataSeeder.DemoSeasonId, default);
        Assert.NotNull(audience);
        Assert.Equal(await db.WorkAreas.CountAsync(), audience.Count);
        Assert.All(audience, area => Assert.Equal(ParishCoordinationDataSeeder.DemoParishId, area.ParishId));
        Assert.Null(await query.GetAsync(Guid.NewGuid(), ParishCoordinationDataSeeder.DemoSeasonId, default));
        Assert.Null(await query.GetAsync(ParishCoordinationDataSeeder.DemoParishId, Guid.NewGuid(), default));
    }
}
