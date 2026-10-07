using FluentAssertions;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;
using Xunit;

namespace Identity.UnitTests.Domain;

public sealed class AppUserTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static readonly Role VolunteerRole = Role.Create(RoleCodes.Volunteer, "Volunteer");

    [Fact]
    public void Create_normalizes_email_to_lower_case()
    {
        var user = AppUser.Create("Nguyen Van A", "NGUYEN@EXAMPLE.COM", null, Guid.NewGuid(), VolunteerRole, Now);

        user.Email.Should().Be("nguyen@example.com");
        user.Status.Should().Be(UserStatus.Active);
        user.Role.Should().BeSameAs(VolunteerRole);
    }

    [Fact]
    public void Create_requires_parish_for_non_admin_role()
    {
        var act = () => AppUser.Create("Nguyen Van A", "nguyen@example.com", null, null, VolunteerRole, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("USER_PARISH_REQUIRED");
    }

    [Fact]
    public void Create_allows_null_parish_for_admin()
    {
        var adminRole = Role.Create(RoleCodes.Admin, "Administrator");
        var user = AppUser.Create("He Quan Tri", "admin@example.com", null, null, adminRole, Now);

        user.ParishId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_empty_full_name(string fullName)
    {
        var act = () => AppUser.Create(fullName, "nguyen@example.com", null, Guid.NewGuid(), VolunteerRole, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("USER_FULL_NAME_INVALID");
    }

    [Fact]
    public void Create_rejects_invalid_email()
    {
        var act = () => AppUser.Create("Nguyen Van A", "not-an-email", null, Guid.NewGuid(), VolunteerRole, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("AUTH_EMAIL_INVALID");
    }

    [Fact]
    public void NormalizeEmail_trims_and_lower_cases()
    {
        AppUser.NormalizeEmail("  A@B.COM  ").Should().Be("a@b.com");
    }

    [Fact]
    public void SetPasswordHash_requires_a_hash()
    {
        var user = AppUser.Create("Nguyen Van A", "nguyen@example.com", null, Guid.NewGuid(), VolunteerRole, Now);

        var act = () => user.SetPasswordHash("   ", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("AUTH_PASSWORD_HASH_REQUIRED");
    }

    [Fact]
    public void Lock_then_Unlock_transitions_status()
    {
        var user = AppUser.Create("Nguyen Van A", "nguyen@example.com", null, Guid.NewGuid(), VolunteerRole, Now);

        user.Lock(Now);
        user.Status.Should().Be(UserStatus.Locked);
        user.CanAuthenticate.Should().BeFalse();

        user.Unlock(Now);
        user.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public void Deactivate_sets_inactive_status()
    {
        var user = AppUser.Create("Nguyen Van A", "nguyen@example.com", null, Guid.NewGuid(), VolunteerRole, Now);

        user.Deactivate(Now);
        user.Status.Should().Be(UserStatus.Inactive);
        user.CanAuthenticate.Should().BeFalse();
    }

    [Fact]
    public void UpdateProfile_updates_full_name_and_normalizes_phone()
    {
        var user = AppUser.Create("Nguyen Van A", "nguyen@example.com", null, Guid.NewGuid(), VolunteerRole, Now);

        user.UpdateProfile("  Tran Van B ", " 0901234567 ", Now);

        user.FullName.Should().Be("Tran Van B");
        user.Phone.Should().Be("0901234567");
    }

    [Fact]
    public void UpdateProfile_rejects_overlong_phone()
    {
        var user = AppUser.Create("Nguyen Van A", "nguyen@example.com", null, Guid.NewGuid(), VolunteerRole, Now);

        var act = () => user.UpdateProfile(null, new string('1', 31), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("USER_PHONE_INVALID");
    }

    [Fact]
    public void CreateProfile_creates_the_volunteer_profile_once()
    {
        var user = AppUser.Create("Nguyen Van A", "nguyen@example.com", null, Guid.NewGuid(), VolunteerRole, Now);
        var communityId = Guid.NewGuid();

        user.CreateProfile(communityId, "Gioi thieu"); 
        user.CreateProfile(communityId, "Gioi thieu khac");

        user.VolunteerProfile.Should().NotBeNull();
        user.VolunteerProfile!.CommunityId.Should().Be(communityId);
        user.VolunteerProfile!.Introduction.Should().Be("Gioi thieu");
    }
}