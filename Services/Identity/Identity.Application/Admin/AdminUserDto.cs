using Identity.Domain.Entities;
using Identity.Domain.Enums;

namespace Identity.Application.Admin;

/// <summary>
/// <c>User</c> schema as seen by ADMIN (API Contract 6, Identity) plus audit timestamps and the
/// optimistic concurrency <c>version</c> to send back on updates (API Contract 4.5).
/// </summary>
public sealed record AdminUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? Phone,
    UserStatus Status,
    Guid? ParishId,
    string Role,
    Guid? CommunityId,
    bool HasPassword,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    long Version)
{
    public static AdminUserDto From(AppUser user)
    {
        return new AdminUserDto(
            user.UserId,
            user.Email,
            user.FullName,
            user.Phone,
            user.Status,
            user.ParishId,
            user.Role?.Code ?? string.Empty,
            user.VolunteerProfile?.CommunityId,
            user.PasswordHash is not null,
            user.CreatedAt,
            user.UpdatedAt,
            user.Version);
    }
}

public sealed record RoleDto(Guid Id, string Code, string Name, string? Description)
{
    public static RoleDto From(Role role) => new(role.RoleId, role.Code, role.Name, role.Description);
}
