namespace ParishCoordination.Application.Seasons;

public sealed class SeasonYearConflictException(Exception innerException)
    : Exception("A season with the same year already exists in this parish.", innerException);