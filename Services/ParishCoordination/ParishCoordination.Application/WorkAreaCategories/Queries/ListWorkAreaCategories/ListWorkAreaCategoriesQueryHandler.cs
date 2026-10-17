namespace ParishCoordination.Application.WorkAreaCategories.Queries.ListWorkAreaCategories;

public sealed class ListWorkAreaCategoriesQueryHandler(
    IWorkAreaCategoryRepository repository)
{
    public async Task<IReadOnlyList<WorkAreaCategoryDto>> HandleAsync(
        ListWorkAreaCategoriesQuery query,
        CancellationToken cancellationToken = default)
    {
        var categories = await repository.GetAllAsync(
            query.Status,
            cancellationToken);

        return categories
            .Select(WorkAreaCategoryDto.From)
            .ToList();
    }
}
