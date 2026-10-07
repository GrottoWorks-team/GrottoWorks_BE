using Identity.Domain.Enums;

namespace Identity.Application.Accounts;

/// <summary>User representation returned by the User tag endpoints (API Contract, schema User).</summary>
public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? Phone,
    UserStatus Status,
    Guid? ParishId,
    string Role)
{
    public static UserDto From(Domain.Entities.AppUser user)
    {
        return new UserDto(
            user.UserId,
            user.Email,
            user.FullName,
            user.Phone,
            user.Status,
            user.ParishId,
            user.Role?.Code ?? string.Empty);
    }
}
