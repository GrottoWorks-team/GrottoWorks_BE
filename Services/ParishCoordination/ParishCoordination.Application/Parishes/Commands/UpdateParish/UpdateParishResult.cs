namespace ParishCoordination.Application.Parishes.Commands.UpdateParish;

public enum UpdateParishOutcome
{
    Success,
    NotFound,
    VersionMismatch
}

public sealed record UpdateParishResult(
    UpdateParishOutcome Outcome,
    ParishDto? Parish);
