namespace Identity.Application.Abstractions;

/// <summary>
/// Identity of the caller extracted from the validated JWT.
/// Interim local abstraction — replaced by <c>BuildingBlocks.Security.ICurrentUser</c> (F-PLT-04) when it lands.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }

    string Role { get; }

    Guid? ParishId { get; }

    Guid? CommunityId { get; }

    bool IsAuthenticated { get; }
}
