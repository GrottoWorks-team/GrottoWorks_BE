namespace BuildingBlocks.Security;

/// <summary>
/// Caller identity resolved from the JWT claims contract (BuildingBlocks README §1):
/// literal claim names, no inbound mapping.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }

    /// <summary>Role code: ADMIN, PARISH, LEADER, MO, VOLUNTEER.</summary>
    string Role { get; }

    /// <summary>Tenant parish; null only for ADMIN (BR-67).</summary>
    Guid? ParishId { get; }

    /// <summary>Current community of the volunteer, when assigned.</summary>
    Guid? CommunityId { get; }
}
