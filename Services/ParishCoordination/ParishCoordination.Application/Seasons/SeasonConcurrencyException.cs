namespace ParishCoordination.Application.Seasons;

public sealed class SeasonConcurrencyException(Exception innerException)
    : Exception("The season was changed by another request.", innerException);
