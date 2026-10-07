namespace ParishCoordination.Application.Parishes.Commands.CreateParish;

public sealed record CreateParishCommand(
    string Name,
    string? Address,
    string? Description);
