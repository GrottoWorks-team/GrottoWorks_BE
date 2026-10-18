using Identity.Domain;
using Identity.Domain.Entities;

namespace Identity.UnitTests;

/// <summary>Builders for S2 tests: one role instance per code, seeded into the fake store.</summary>
public static class TestUsers
{
    public static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public static Dictionary<string, Role> SeedRoles(FakeUserStore users)
    {
        var roles = RoleCodes.All.ToDictionary(code => code, code => Role.Create(code, code));
        foreach (var role in roles.Values)
        {
            users.SeedRole(role);
        }

        return roles;
    }

    public static AppUser Add(
        FakeUserStore users,
        Role role,
        string email,
        Guid? parishId,
        string name = "Nguyen Van A",
        bool withPassword = true)
    {
        var user = AppUser.Create(name, email, null, parishId, role, Now);
        if (withPassword)
        {
            user.SetPasswordHash("hash:Volunteer123", Now);
        }

        users.Seed(user);
        return user;
    }
}
