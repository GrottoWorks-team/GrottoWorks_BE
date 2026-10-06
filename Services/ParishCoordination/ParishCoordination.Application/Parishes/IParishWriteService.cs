namespace ParishCoordination.Application.Parishes;

public interface IParishWriteService
{
    Task<ParishResponse> CreateAsync(
        CreateParishCommand command,
        CancellationToken cancellationToken = default);
}
