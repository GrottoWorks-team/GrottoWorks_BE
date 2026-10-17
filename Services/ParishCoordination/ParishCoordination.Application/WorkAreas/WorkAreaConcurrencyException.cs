namespace ParishCoordination.Application.WorkAreas;

public sealed class WorkAreaConcurrencyException(Exception innerException)
    : Exception("The work area was changed by another request.", innerException);
