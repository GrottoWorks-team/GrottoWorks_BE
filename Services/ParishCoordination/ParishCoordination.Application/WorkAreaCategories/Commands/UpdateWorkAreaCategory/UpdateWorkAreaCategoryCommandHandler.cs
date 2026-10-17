namespace ParishCoordination.Application.WorkAreaCategories.Commands.UpdateWorkAreaCategory;

public sealed class UpdateWorkAreaCategoryCommandHandler(
    IWorkAreaCategoryRepository repository)
{
    public async Task<UpdateWorkAreaCategoryResult> HandleAsync(
        UpdateWorkAreaCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        var category = await repository.GetByIdForUpdateAsync(
            command.CategoryId,
            cancellationToken);

        if (category is null)
        {
            return UpdateWorkAreaCategoryResult.NotFound();
        }

        var name = command.Name?.Trim() ?? category.Name;
        var description = command.ShouldUpdateDescription
            ? command.Description
            : category.Description;
        var status = command.Status ?? category.Status;

        category.Update(name, description, status);
        await repository.SaveChangesAsync(cancellationToken);

        return UpdateWorkAreaCategoryResult.Success(
            WorkAreaCategoryDto.From(category));
    }
}
