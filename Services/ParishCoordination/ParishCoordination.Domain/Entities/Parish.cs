using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Domain.Entities;

public sealed class Parish
{
    private Parish()
    {
    }

    private Parish(
        Guid id,
        string name,
        string? address,
        string? description,
        ParishStatus status,
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
        Version = 1;
    }

    public static Parish Create(
        string name,
        string? address,
        string? description,
        DateTimeOffset createdAtUtc)
    {
        return new Parish(
            Guid.NewGuid(),
            name,
            address,
            description,
            ParishStatus.Active,
            createdAtUtc);
    }

    public void Update(
        string name,
        string? address,
        string? description,
        ParishStatus status,
        DateTimeOffset updatedAtUtc)
    {
        Name = name;
        Address = address;
        Description = description;
        Status = status;
        UpdatedAt = updatedAtUtc;
        Version++;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Address { get; private set; }

    public string? Description { get; private set; }

    public ParishStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public int Version { get; private set; }
}
