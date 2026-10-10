namespace ParishCoordination.Application.Communities;

public sealed class CommunityNameConflictException(Exception innerException)
    : Exception("A community with the same name already exists in this parish.", innerException);
