using Identity.Domain.Entities;
using Identity.Domain.Enums;

namespace Identity.Application.Accounts;

public sealed record AvailabilitySlotDto(
    DateTimeOffset AvailableFrom,
    DateTimeOffset AvailableTo,
    string? Note)
{
    public static AvailabilitySlotDto From(VolunteerAvailability availability)
    {
        return new AvailabilitySlotDto(
            availability.AvailableFrom,
            availability.AvailableTo,
            availability.AvailabilityNote);
    }
}

/// <summary>Current user profile (F-IDN-05): account data plus volunteer details when present.</summary>
public sealed record UserProfileDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? Phone,
    UserStatus Status,
    Guid? ParishId,
    string Role,
    Guid? CommunityId,
    string? Introduction,
    IReadOnlyList<Guid> SkillIds,
    IReadOnlyList<AvailabilitySlotDto> Availability)
{
    public static UserProfileDto From(AppUser user)
    {
        var profile = user.VolunteerProfile;

        return new UserProfileDto(
            user.UserId,
            user.Email,
            user.FullName,
            user.Phone,
            user.Status,
            user.ParishId,
            user.Role?.Code ?? string.Empty,
            profile?.CommunityId,
            profile?.Introduction,
            user.VolunteerSkills
                .Select(link => link.SkillId)
                .OrderBy(skillId => skillId)
                .ToList(),
            user.VolunteerAvailabilities
                .OrderBy(slot => slot.AvailableFrom)
                .Select(AvailabilitySlotDto.From)
                .ToList());
    }
}
