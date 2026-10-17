using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Application.WorkAreaCategories.Queries.ListWorkAreaCategories;

public sealed record ListWorkAreaCategoriesQuery(WorkAreaCategoryStatus? Status);
