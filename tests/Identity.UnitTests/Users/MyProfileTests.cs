using FluentAssertions;
using Identity.Application.Users.GetMyProfile;
using Identity.Application.Users.UpdateMyProfile;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;
using Xunit;

namespace Identity.UnitTests.Users;

public sealed class MyProfileTests
{
    private static readonly Guid ParishId = Guid.NewGuid();
    private static readonly Guid CommunityId = Guid.NewGuid();

    private static AppUser CreateOwnUser()
    {
        var role = Role.Create(RoleCodes.Volunteer, "Volunteer");
        var user = AppUser.Create("Nguyen Van A", "nguyen@example.com", null, ParishId, role, DateTimeOffset.UtcNow);
        user.SetPasswordHash("hash:Volunteer123", DateTimeOffset.UtcNow);
        return user;
    }

    [Fact]
    public async Task GetMyProfile_returns_the_callers_profile()
    {
        var users = new FakeUserStore();
        var user = CreateOwnUser();
        users.Seed(user);

        var profile = await new GetMyProfileQueryHandler(
            new FakeCurrentUser(user.UserId),
            users).HandleAsync(new GetMyProfileQuery(), CancellationToken.None);

        profile.Id.Should().Be(user.UserId);
        profile.Email.Should().Be("nguyen@example.com");
        profile.ParishId.Should().Be(ParishId);
    }

    [Fact]
    public async Task GetMyProfile_reports_not_found_when_the_user_is_missing()
    {
        var users = new FakeUserStore();

        var act = async () => await new GetMyProfileQueryHandler(
            new FakeCurrentUser(Guid.NewGuid()),
            users).HandleAsync(new GetMyProfileQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "USER_NOT_FOUND");
    }

    [Fact]
    public async Task UpdateMyProfile_creates_a_volunteer_profile_on_first_update()
    {
        var users = new FakeUserStore();
        var user = CreateOwnUser();
        users.Seed(user);

        var profile = await new UpdateMyProfileCommandHandler(
            new FakeCurrentUser(user.UserId),
            users,
            new FakeParishDirectory()).HandleAsync(
                new UpdateMyProfileCommand("Tran Van B", "0901234567", "Gioi thieu", CommunityId),
                CancellationToken.None);

        profile.DisplayName.Should().Be("Tran Van B");
        profile.Phone.Should().Be("0901234567");
        profile.CommunityId.Should().Be(CommunityId);
        profile.Introduction.Should().Be("Gioi thieu");
    }

    [Fact]
    public async Task UpdateMyProfile_partial_update_keeps_other_fields()
    {
        var users = new FakeUserStore();
        var user = CreateOwnUser();
        users.Seed(user);
        var currentUser = new FakeCurrentUser(user.UserId);

        await new UpdateMyProfileCommandHandler(currentUser, users, new FakeParishDirectory()).HandleAsync(
            new UpdateMyProfileCommand(null, null, null, CommunityId),
            CancellationToken.None);

        var profile = await new GetMyProfileQueryHandler(currentUser, users)
            .HandleAsync(new GetMyProfileQuery(), CancellationToken.None);

        profile.DisplayName.Should().Be("Nguyen Van A");
        profile.CommunityId.Should().Be(CommunityId);
    }

    [Fact]
    public async Task UpdateMyProfile_requires_a_community_before_an_introduction()
    {
        var users = new FakeUserStore();
        var user = CreateOwnUser();
        users.Seed(user);

        var act = async () => await new UpdateMyProfileCommandHandler(
            new FakeCurrentUser(user.UserId),
            users,
            new FakeParishDirectory()).HandleAsync(
                new UpdateMyProfileCommand(null, null, "Gioi thieu", null),
                CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "PROFILE_COMMUNITY_REQUIRED");
    }

    [Fact]
    public async Task UpdateMyProfile_rejects_a_community_outside_the_callers_parish()
    {
        var users = new FakeUserStore();
        var user = CreateOwnUser();
        users.Seed(user);
        var directory = new FakeParishDirectory(allow: false);

        var act = async () => await new UpdateMyProfileCommandHandler(
            new FakeCurrentUser(user.UserId),
            users,
            directory).HandleAsync(
                new UpdateMyProfileCommand(null, null, null, CommunityId),
                CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "PROFILE_COMMUNITY_NOT_IN_PARISH");
        directory.Checks.Should().ContainSingle().Which.Should().Be((CommunityId, ParishId));
        user.VolunteerProfile.Should().BeNull();
        users.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateMyProfile_does_not_recheck_an_unchanged_community()
    {
        var users = new FakeUserStore();
        var user = CreateOwnUser();
        user.CreateProfile(CommunityId);
        users.Seed(user);
        var directory = new FakeParishDirectory(allow: false);

        var profile = await new UpdateMyProfileCommandHandler(
            new FakeCurrentUser(user.UserId),
            users,
            directory).HandleAsync(
                new UpdateMyProfileCommand(null, null, "Gioi thieu", CommunityId),
                CancellationToken.None);

        profile.Introduction.Should().Be("Gioi thieu");
        directory.Checks.Should().BeEmpty();
    }
}
