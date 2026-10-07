using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Domain;
using Identity.Domain.Entities;

namespace Identity.Application.Users.UpdateMyProfile;

/// <summary>PATCH semantics: <c>null</c> means "leave unchanged"; there is no field clearing in F-IDN-05.</summary>
public sealed record UpdateMyProfileCommand(
    string? DisplayName,
    string? Phone,
    string? Introduction,
    Guid? CommunityId);

/// <summary>F-IDN-05: update own account data and volunteer details.</summary>
public sealed class UpdateMyProfileCommandHandler(
    ICurrentUser currentUser,
    IUserAccountStore userAccountStore)
{
    public async Task<UserProfileDto> HandleAsync(
        UpdateMyProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        var user = await userAccountStore.GetDetailsByIdAsync(currentUser.UserId, cancellationToken)
            ?? throw new DomainException("USER_NOT_FOUND", "The user no longer exists.");

        var now = DateTimeOffset.UtcNow;
        user.UpdateProfile(command.DisplayName, command.Phone, now);

        var profile = user.VolunteerProfile;
        if (profile is null)
        {
            if (command.CommunityId is { } communityId)
            {
                user.CreateProfile(communityId, command.Introduction);
            }
            else if (command.Introduction is not null)
            {
                throw new DomainException(
                    "PROFILE_COMMUNITY_REQUIRED",
                    "A community is required before volunteer details can be saved.");
            }
        }
        else
        {
            profile.Update(command.CommunityId, command.Introduction, availabilityNote: null);
        }

        await userAccountStore.SaveChangesAsync(cancellationToken);

        return UserProfileDto.From(user);
    }
}
