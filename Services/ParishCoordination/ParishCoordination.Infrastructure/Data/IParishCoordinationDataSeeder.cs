namespace ParishCoordination.Infrastructure.Data;

public interface IParishCoordinationDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}