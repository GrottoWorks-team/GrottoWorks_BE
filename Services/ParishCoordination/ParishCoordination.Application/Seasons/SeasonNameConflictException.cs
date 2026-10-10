namespace ParishCoordination.Application.Seasons;

public sealed class SeasonNameConflictException(Exception innerException)
    : Exception("A season with the same name already exists in this parish.", innerException);