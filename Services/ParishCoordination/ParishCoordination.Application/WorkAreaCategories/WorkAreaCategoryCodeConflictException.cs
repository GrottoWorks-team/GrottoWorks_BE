namespace ParishCoordination.Application.WorkAreaCategories;

public sealed class WorkAreaCategoryCodeConflictException(Exception innerException)
    : Exception("A work area category with this code already exists.", innerException);
