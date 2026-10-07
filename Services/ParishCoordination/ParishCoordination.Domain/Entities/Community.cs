using ParishCoordination.Domain.Enums;

namespace ParishCoordination.Domain.Entities;

public sealed class Community
{
    private Community()
    {
    }

    public Guid Id { get; private set; }

    public Guid ParishId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public CommunityStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public int Version { get; private set; } = 1;
}
