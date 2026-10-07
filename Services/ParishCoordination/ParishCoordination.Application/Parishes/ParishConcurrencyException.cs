namespace ParishCoordination.Application.Parishes;

public sealed class ParishConcurrencyException(Exception innerException)
    : Exception("The parish was changed by another request.", innerException);
