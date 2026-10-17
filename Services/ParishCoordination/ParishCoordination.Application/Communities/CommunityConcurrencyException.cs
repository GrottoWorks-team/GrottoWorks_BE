namespace ParishCoordination.Application.Communities;

public sealed class CommunityConcurrencyException(Exception innerException)
    : Exception("The community was changed by another request.", innerException);
