using ParishCoordination.Domain.Entities;

namespace ParishCoordination.Application.WorkAreaCategories.Commands.CreateWorkAreaCategory;

public sealed class CreateWorkAreaCategoryCommandHandler(
    IWorkAreaCategoryRepository repository)
{
    public async Task<CreateWorkAreaCategoryResult> HandleAsync(
        CreateWorkAreaCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = WorkAreaCategory.NormalizeCode(command.Code);

        var codeExists = await repository.CodeExistsAsync(
            normalizedCode,
            cancellationToken);

        if (codeExists)
        {
            return CreateWorkAreaCategoryResult.CodeConflict();
        }

        var category = WorkAreaCategory.Create(
            normalizedCode,
            command.Name,
            command.Description);

        repository.Add(category);

        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (WorkAreaCategoryCodeConflictException)
        {
            return CreateWorkAreaCategoryResult.CodeConflict();
        }

        return CreateWorkAreaCategoryResult.Success(
            WorkAreaCategoryDto.From(category));
    }
}
