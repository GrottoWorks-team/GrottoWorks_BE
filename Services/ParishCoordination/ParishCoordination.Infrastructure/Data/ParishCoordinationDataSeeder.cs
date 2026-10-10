using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Infrastructure.Data;

public sealed class ParishCoordinationDataSeeder(
    ParishCoordinationDbContext dbContext,
    ILogger<ParishCoordinationDataSeeder> logger) : IParishCoordinationDataSeeder
{
    public static readonly Guid DemoParishId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid DemoCommunityNorthId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid DemoCommunitySouthId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var changed = false;
        var parish = await dbContext.Parishes.FindAsync(
            new object[] { DemoParishId },
            cancellationToken);

        if (parish is null)
        {
            parish = Parish.Create(
                DemoParishId,
                "GrottoWorks Demo Parish",
                "Demo address",
                "Seed parish for local development.",
                DateTimeOffset.UtcNow);
            dbContext.Parishes.Add(parish);
            changed = true;
        }

        if (await dbContext.Communities.FindAsync(
                new object[] { DemoCommunityNorthId },
                cancellationToken) is null)
        {
            dbContext.Communities.Add(Community.Create(
                DemoCommunityNorthId,
                DemoParishId,
                "Demo Community North",
                "Seed community for local development.",
                DateTimeOffset.UtcNow));
            changed = true;
        }

        if (await dbContext.Communities.FindAsync(
                new object[] { DemoCommunitySouthId },
                cancellationToken) is null)
        {
            dbContext.Communities.Add(Community.Create(
                DemoCommunitySouthId,
                DemoParishId,
                "Demo Community South",
                "Seed community for local development.",
                DateTimeOffset.UtcNow));
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("ParishCoordination demo data seeding completed.");
    }
}