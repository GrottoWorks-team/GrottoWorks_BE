using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;

namespace Identity.Application.Users.ListParishUsers;

/// <summary><c>UserSummary</c>: what a coordinator needs to pick and contact a person of the parish.</summary>
public sealed record UserSummaryDto(
    Guid Id,
    string DisplayName,
    string Email,
    string? Phone,
    string Role,
    UserStatus Status,
    Guid? CommunityId,
    IReadOnlyList<Guid> SkillIds)
{
    public static UserSummaryDto From(AppUser user)
    {
        return new UserSummaryDto(
            user.UserId,
            user.FullName,
            user.Email,
            user.Phone,
            user.Role?.Code ?? string.Empty,
            user.Status,
            user.VolunteerProfile?.CommunityId,
            user.VolunteerSkills.Select(link => link.SkillId).OrderBy(id => id).ToList());
    }
}

/// <summary>
/// F-IDN-10 <c>listParishUsers</c> (PARISH, LEADER, MO): users of the caller's parish only (BR-67).
/// The parish comes from the token and overrides anything the client could send, so the scope cannot
/// be widened through query parameters (API Contract 1).
/// </summary>
public sealed class ListParishUsersQueryHandler(
    ICurrentUser currentUser,
    IUserAccountStore userAccountStore)
{
    public async Task<PagedResult<UserSummaryDto>> HandleAsync(
        UserSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.ParishId is not { } parishId)
        {
            throw new DomainException(
                "USER_PARISH_SCOPE_REQUIRED",
                "Your account is not attached to a parish.");
        }

        var result = await userAccountStore.SearchAsync(criteria with { ParishId = parishId }, cancellationToken);

        return new PagedResult<UserSummaryDto>(
            result.Items.Select(UserSummaryDto.From).ToList(),
            result.TotalItems);
    }
}
