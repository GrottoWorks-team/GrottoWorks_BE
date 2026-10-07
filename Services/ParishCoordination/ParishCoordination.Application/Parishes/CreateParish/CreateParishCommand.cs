namespace ParishCoordination.Application.Parishes.CreateParish;

public sealed record CreateParishCommand(
    string Name,
    string? Address,
    string? Description);
