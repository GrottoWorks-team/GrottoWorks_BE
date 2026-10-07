namespace Identity.Domain;

/// <summary>
/// Canonical role codes (one role per account, ERD v3.5 <c>app_user.role_id</c>).
/// These values are also the values of the JWT <c>role</c> claim.
/// </summary>
public static class RoleCodes
{
    public const string Admin = "ADMIN";
    public const string Parish = "PARISH";
    public const string Leader = "LEADER";
    public const string MaterialOfficer = "MO";
    public const string Volunteer = "VOLUNTEER";

    public static readonly IReadOnlyList<string> All =
    [
        Admin,
        Parish,
        Leader,
        MaterialOfficer,
        Volunteer
    ];

    public static bool IsValid(string? code) =>
        code is not null && All.Contains(code, StringComparer.Ordinal);
}
