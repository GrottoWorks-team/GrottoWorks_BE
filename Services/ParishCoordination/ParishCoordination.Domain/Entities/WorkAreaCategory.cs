using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Domain.Entities;

public sealed class WorkAreaCategory
{
    private WorkAreaCategory()
    {
    }

    public static WorkAreaCategory Create(
        Guid id,
        string code,
        string name,
        string? description,
        WorkAreaCategoryStatus status)
    {
        return new WorkAreaCategory
        {
            Id = id,
            Code = code,
            Name = name,
            Description = description,
            Status = status
        };
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public WorkAreaCategoryStatus Status { get; private set; }
}
