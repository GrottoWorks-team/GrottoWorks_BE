namespace ParishCoordination.Application.Parishes;

public interface IParishReadService
{
    Task<IReadOnlyList<ParishResponse>> GetAllAsync(
        CancellationToken cancellationToken = default);
}
