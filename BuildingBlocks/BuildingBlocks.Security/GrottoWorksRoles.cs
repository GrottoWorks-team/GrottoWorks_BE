namespace BuildingBlocks.Security;

/// <summary>Role codes (BuildingBlocks README §1). Must stay in sync with the Identity seed.</summary>
public static class GrottoWorksRoles
{
    public const string Admin = "ADMIN";
    public const string Parish = "PARISH";
    public const string Leader = "LEADER";
    public const string MaterialOfficer = "MO";
    public const string Volunteer = "VOLUNTEER";

    public static readonly string[] All = [Admin, Parish, Leader, MaterialOfficer, Volunteer];
}
