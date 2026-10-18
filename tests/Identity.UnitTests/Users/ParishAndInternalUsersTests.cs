using FluentAssertions;
using Identity.Application.Accounts;
using Identity.Application.Users.GetInternalUsers;
using Identity.Application.Users.ListParishUsers;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;
using Xunit;

namespace Identity.UnitTests.Users;

/// <summary>F-IDN-10 listParishUsers (tenant scope) and F-IDN-09 internal lookup.</summary>
public sealed class ParishAndInternalUsersTests
{
    private static readonly Guid ParishA = Guid.NewGuid();
    private static readonly Guid ParishB = Guid.NewGuid();
    private static readonly Guid CommunityA = Guid.NewGuid();

    private readonly FakeUserStore _users = new();
    private readonly Dictionary<string, Role> _roles;
    private readonly AppUser _leader;
    private readonly AppUser _volunteerA;
    private readonly AppUser _volunteerB;
    private readonly Guid _skill = Guid.NewGuid();

    public ParishAndInternalUsersTests()
    {
        _roles = TestUsers.SeedRoles(_users);
        _leader = TestUsers.Add(_users, _roles[RoleCodes.Leader], "leader@example.com", ParishA, "Le Leader");
        _volunteerA = TestUsers.Add(_users, _roles[RoleCodes.Volunteer], "va@example.com", ParishA, "An Volunteer");
        _volunteerA.CreateProfile(CommunityA);
        _volunteerA.ReplaceSkills([_skill], TestUsers.Now);
        _volunteerB = TestUsers.Add(_users, _roles[RoleCodes.Volunteer], "vb@example.com", ParishB, "Binh Volunteer");
    }

    private ListParishUsersQueryHandler ParishHandler(Guid? parishId) =>
        new(new FakeCurrentUser(_leader.UserId, RoleCodes.Leader, parishId), _users);

    [Fact]
    public async Task ListParishUsers_only_returns_users_of_the_callers_parish()
    {
        var result = await ParishHandler(ParishA).HandleAsync(new UserSearchCriteria(1, 20));

        result.TotalItems.Should().Be(2);
        result.Items.Select(user => user.Id).Should().BeEquivalentTo([_leader.UserId, _volunteerA.UserId]);
    }

    [Fact]
    public async Task ListParishUsers_overrides_a_parish_sent_by_the_client()
    {
        await ParishHandler(ParishA).HandleAsync(new UserSearchCriteria(1, 20, ParishId: ParishB));

        _users.LastSearch!.ParishId.Should().Be(ParishA, "the token parish wins (API Contract 1)");
    }

    [Fact]
    public async Task ListParishUsers_filters_by_role_community_and_skill()
    {
        var byRole = await ParishHandler(ParishA).HandleAsync(new UserSearchCriteria(1, 20, RoleCode: RoleCodes.Volunteer));
        var byCommunity = await ParishHandler(ParishA).HandleAsync(new UserSearchCriteria(1, 20, CommunityId: CommunityA));
        var bySkill = await ParishHandler(ParishA).HandleAsync(new UserSearchCriteria(1, 20, SkillId: _skill));

        byRole.Items.Should().ContainSingle().Which.Id.Should().Be(_volunteerA.UserId);
        byCommunity.Items.Should().ContainSingle().Which.CommunityId.Should().Be(CommunityA);
        bySkill.Items.Should().ContainSingle().Which.SkillIds.Should().Equal(_skill);
    }

    [Fact]
    public async Task ListParishUsers_without_parish_claim_is_forbidden()
    {
        var act = () => ParishHandler(null).HandleAsync(new UserSearchCriteria(1, 20));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("USER_PARISH_SCOPE_REQUIRED");
    }

    [Fact]
    public async Task InternalUsers_returns_requested_users_in_request_order_and_omits_unknown_ids()
    {
        var handler = new GetInternalUsersQueryHandler(_users);

        var users = await handler.HandleAsync(new GetInternalUsersQuery(
            [_volunteerB.UserId, Guid.NewGuid(), _volunteerA.UserId, _volunteerA.UserId]));

        users.Select(user => user.UserId).Should().Equal(_volunteerB.UserId, _volunteerA.UserId);
        var a = users[1];
        a.Status.Should().Be(UserStatus.Active);
        a.Role.Should().Be(RoleCodes.Volunteer);
        a.ParishId.Should().Be(ParishA);
        a.CommunityId.Should().Be(CommunityA);
        a.SkillIds.Should().Equal(_skill);
    }

    [Fact]
    public async Task InternalUsers_requires_between_one_and_100_ids()
    {
        var handler = new GetInternalUsersQueryHandler(_users);

        var none = () => handler.HandleAsync(new GetInternalUsersQuery([]));
        var tooMany = () => handler.HandleAsync(new GetInternalUsersQuery(
            Enumerable.Range(0, GetInternalUsersQueryHandler.MaxIds + 1).Select(_ => Guid.NewGuid()).ToList()));

        (await none.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("INTERNAL_USERS_IDS_REQUIRED");
        (await tooMany.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("INTERNAL_USERS_TOO_MANY_IDS");
    }
}
