using Identity.Application.Accounts;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;

namespace Identity.Application.Users.GetInternalUsers;

public sealed record GetInternalUsersQuery(IReadOnlyList<Guid> UserIds);

/// <summary>
/// Service-to-service user view (F-IDN-09): Parish checks a leader/officer is ACTIVE with the right role,
/// Task checks status, community and skills before assigning. Kept free of contact data.
/// </summary>
public sealed record InternalUserDto(
    Guid UserId,
    string DisplayName,
    UserStatus Status,
    string Role,
    Guid? ParishId,
    Guid? CommunityId,
    IReadOnlyList<Guid> SkillIds)
{
    public static InternalUserDto From(AppUser user)
    {
        return new InternalUserDto(
            user.UserId,
            user.FullName,
            user.Status,
            user.Role?.Code ?? string.Empty,
            user.ParishId,
            user.VolunteerProfile?.CommunityId,
            user.VolunteerSkills.Select(link => link.SkillId).OrderBy(id => id).ToList());
    }
}

/// <summary>
/// F-IDN-09 <c>GET /internal/users?ids=</c>. Unknown ids are omitted (callers compare the returned ids).
/// At most <see cref="MaxIds"/> ids per call.
/// </summary>
public sealed class GetInternalUsersQueryHandler(IUserAccountStore userAccountStore)
{
    public const int MaxIds = 100;

    public async Task<IReadOnlyList<InternalUserDto>> HandleAsync(
        GetInternalUsersQuery query,
        CancellationToken cancellationToken = default)
    {
        var ids = query.UserIds.Where(id => id != Guid.Empty).Distinct().ToList();

        if (ids.Count == 0)
        {
            throw new DomainException("INTERNAL_USERS_IDS_REQUIRED", "At least one user id is required.");
        }

        if (ids.Count > MaxIds)
        {
            throw new DomainException(
                "INTERNAL_USERS_TOO_MANY_IDS",
                $"At most {MaxIds} user ids can be requested at once.");
        }

        var users = await userAccountStore.GetDetailsByIdsAsync(ids, cancellationToken);

        return users
            .OrderBy(user => ids.IndexOf(user.UserId))
            .Select(InternalUserDto.From)
            .ToList();
    }
}
