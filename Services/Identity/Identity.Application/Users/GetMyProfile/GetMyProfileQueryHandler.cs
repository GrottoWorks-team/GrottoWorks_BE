using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Domain;

namespace Identity.Application.Users.GetMyProfile;

public sealed record GetMyProfileQuery;

/// <summary>F-IDN-05: profile of the authenticated caller.</summary>
public sealed class GetMyProfileQueryHandler(
    ICurrentUser currentUser,
    IUserAccountStore userAccountStore)
{
    public async Task<UserProfileDto> HandleAsync(
        GetMyProfileQuery query,
        CancellationToken cancellationToken = default)
    {
        var user = await userAccountStore.GetDetailsByIdAsync(currentUser.UserId, cancellationToken)
            ?? throw new DomainException("USER_NOT_FOUND", "The user no longer exists.");

        return UserProfileDto.From(user);
    }
}
