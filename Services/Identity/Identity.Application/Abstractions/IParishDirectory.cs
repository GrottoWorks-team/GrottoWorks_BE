namespace Identity.Application.Abstractions;

/// <summary>
/// Read access to parish/community data owned by ParishCoordination (Lâm).
/// Identity stores <c>community_id</c> without an FK, so ownership must be checked here before it
/// lands in the profile and in the JWT <c>communityId</c> claim (Contract §1: a client cannot expand
/// its own scope by changing request data).
/// </summary>
public interface IParishDirectory
{
    Task<bool> CommunityBelongsToParishAsync(
        Guid communityId,
        Guid parishId,
        CancellationToken cancellationToken = default);
}
