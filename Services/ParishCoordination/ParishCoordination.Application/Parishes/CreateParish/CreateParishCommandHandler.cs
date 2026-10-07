using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Application.Parishes.CreateParish;

public sealed class CreateParishCommandHandler(IParishRepository parishRepository)
{
    public async Task<ParishDto> HandleAsync(
        CreateParishCommand command,
        CancellationToken cancellationToken = default)
    {
        var parish = Parish.Create(
            command.Name,
            command.Address,
            command.Description,
            DateTimeOffset.UtcNow);

        parishRepository.Add(parish);
        await parishRepository.SaveChangesAsync(cancellationToken);

        return ParishDto.From(parish);
    }
}
