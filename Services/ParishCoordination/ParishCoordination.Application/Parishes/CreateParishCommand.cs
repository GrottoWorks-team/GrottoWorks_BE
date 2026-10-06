namespace ParishCoordination.Application.Parishes;

public sealed record CreateParishCommand(
    string Name,
    string? Address,
    string? Description);
