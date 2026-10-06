namespace ParishCoordination.Domain.Entities;

public sealed class Parish
{
    private Parish()
    {
    }

    public Parish(
        Guid id,
        string name,
        string? address,
        string? description,
        string status,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt = null)
    {
        Id = id;
        Name = name;
        Address = address;
        Description = description;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Address { get; private set; }

    public string? Description { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }
}
