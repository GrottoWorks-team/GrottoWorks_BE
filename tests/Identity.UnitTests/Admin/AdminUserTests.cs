using FluentAssertions;
using Identity.Application.Accounts;
using Identity.Application.Admin.AssignUserRole;
using Identity.Application.Admin.CreateUser;
using Identity.Application.Admin.ListAssignableRoles;
using Identity.Application.Admin.ListUsers;
using Identity.Application.Admin.LockUser;
using Identity.Application.Admin.UnlockUser;
using Identity.Application.Admin.UpdateUser;
using Identity.Contracts.Events;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;
using Xunit;

namespace Identity.UnitTests.Admin;

/// <summary>F-IDN-08: ADMIN account management.</summary>
public sealed class AdminUserTests
{
    private static readonly Guid ParishId = Guid.NewGuid();

    private readonly FakeUserStore _users = new();
    private readonly FakeRefreshTokenStore _refreshTokens = new();
    private readonly FakeIntegrationEventPublisher _events = new();
    private readonly Dictionary<string, Role> _roles;
    private readonly AppUser _admin;
    private readonly AppUser _volunteer;

    public AdminUserTests()
    {
        _roles = TestUsers.SeedRoles(_users);
        _admin = TestUsers.Add(_users, _roles[RoleCodes.Admin], "admin@example.com", null, "System Admin");
        _volunteer = TestUsers.Add(_users, _roles[RoleCodes.Volunteer], "volunteer@example.com", ParishId, "Tinh Nguyen");
    }

    private FakeCurrentUser Admin => new(_admin.UserId, RoleCodes.Admin);

    private CreateUserCommandHandler CreateHandler(FakeParishDirectory? parishes = null) =>
        new(_users, parishes ?? new FakeParishDirectory(), new FakePasswordHasher(), _events);

    private UpdateUserCommandHandler UpdateHandler(FakeParishDirectory? parishes = null) =>
        new(Admin, _users, _refreshTokens, parishes ?? new FakeParishDirectory());

    private LockUserCommandHandler LockHandler() => new(Admin, _users, _refreshTokens, _events);

    private UnlockUserCommandHandler UnlockHandler() => new(Admin, _users, _events);

    private AssignUserRoleCommandHandler RoleHandler(FakeParishDirectory? parishes = null) =>
        new(Admin, _users, parishes ?? new FakeParishDirectory());

    private static async Task<string> CodeOf(Func<Task> act) =>
        (await act.Should().ThrowAsync<DomainException>()).Which.Code;

    // ---- createUser -------------------------------------------------------------------------

    [Fact]
    public async Task CreateUser_creates_an_active_parish_account_and_publishes_registered()
    {
        var user = await CreateHandler().HandleAsync(
            new CreateUserCommand("Leader@Example.com", "Truong Khu Vuc", "0900", "leader", ParishId, "Leader1234"));

        user.Email.Should().Be("leader@example.com");
        user.Role.Should().Be(RoleCodes.Leader);
        user.Status.Should().Be(UserStatus.Active);
        user.ParishId.Should().Be(ParishId);
        user.HasPassword.Should().BeTrue();
        _users.AddedUsers.Single().PasswordHash.Should().Be("hash:Leader1234");

        var published = _events.Events.Should().ContainSingle().Which;
        published.EventType.Should().Be(IdentityUserRegistered.EventType);
        ((IdentityUserRegistered)published.Payload!).Role.Should().Be(RoleCodes.Leader);
    }

    [Fact]
    public async Task CreateUser_without_password_creates_an_invitation_account()
    {
        var user = await CreateHandler().HandleAsync(
            new CreateUserCommand("mo@example.com", "Can Bo Vat Tu", null, RoleCodes.MaterialOfficer, ParishId, null));

        user.HasPassword.Should().BeFalse();
        _users.AddedUsers.Single().CanAuthenticate.Should().BeFalse();
    }

    [Fact]
    public async Task CreateUser_enforces_br67_parish_for_non_admin()
    {
        (await CodeOf(() => CreateHandler().HandleAsync(
                new CreateUserCommand("p@example.com", "Ban Phu Trach", null, RoleCodes.Parish, null, null))))
            .Should().Be("USER_PARISH_REQUIRED");

        var admin = await CreateHandler().HandleAsync(
            new CreateUserCommand("admin2@example.com", "Admin Two", null, RoleCodes.Admin, null, "Admin12345"));

        admin.ParishId.Should().BeNull();
        _events.Events.Should().BeEmpty("the v1 registered payload needs a parish");
    }

    [Fact]
    public async Task CreateUser_rejects_duplicate_email_unknown_role_unknown_parish_and_weak_password()
    {
        (await CodeOf(() => CreateHandler().HandleAsync(
            new CreateUserCommand("VOLUNTEER@example.com", "Dup", null, RoleCodes.Volunteer, ParishId, null))))
            .Should().Be("AUTH_EMAIL_ALREADY_EXISTS");

        (await CodeOf(() => CreateHandler().HandleAsync(
            new CreateUserCommand("x@example.com", "X", null, "SUPERUSER", ParishId, null))))
            .Should().Be("ROLE_UNKNOWN");

        (await CodeOf(() => CreateHandler(new FakeParishDirectory(parishExists: false)).HandleAsync(
            new CreateUserCommand("y@example.com", "Y", null, RoleCodes.Volunteer, ParishId, null))))
            .Should().Be("USER_PARISH_UNKNOWN");

        (await CodeOf(() => CreateHandler().HandleAsync(
            new CreateUserCommand("z@example.com", "Z", null, RoleCodes.Volunteer, ParishId, "weak"))))
            .Should().Be("AUTH_PASSWORD_POLICY_VIOLATION");

        _users.AddedUsers.Should().BeEmpty();
    }

    // ---- updateUser -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateUser_changes_contact_data_and_keeps_omitted_fields()
    {
        var updated = await UpdateHandler().HandleAsync(
            new UpdateUserCommand(_volunteer.UserId, null, "NEW@example.com", null, "0911", null, null));

        updated.Email.Should().Be("new@example.com");
        updated.Phone.Should().Be("0911");
        updated.DisplayName.Should().Be("Tinh Nguyen");
    }

    [Fact]
    public async Task UpdateUser_rejects_an_email_taken_by_another_account()
    {
        (await CodeOf(() => UpdateHandler().HandleAsync(
                new UpdateUserCommand(_volunteer.UserId, null, "admin@example.com", null, null, null, null))))
            .Should().Be("AUTH_EMAIL_ALREADY_EXISTS");
    }

    [Fact]
    public async Task UpdateUser_deactivation_revokes_sessions_and_reactivation_works()
    {
        var updated = await UpdateHandler().HandleAsync(
            new UpdateUserCommand(_volunteer.UserId, null, null, null, null, null, UserStatus.Inactive));

        updated.Status.Should().Be(UserStatus.Inactive);
        _refreshTokens.RevokedUsers.Should().ContainSingle().Which.UserId.Should().Be(_volunteer.UserId);

        updated = await UpdateHandler().HandleAsync(
            new UpdateUserCommand(_volunteer.UserId, null, null, null, null, null, UserStatus.Active));
        updated.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public async Task UpdateUser_cannot_lock_through_status_nor_deactivate_self()
    {
        (await CodeOf(() => UpdateHandler().HandleAsync(
                new UpdateUserCommand(_volunteer.UserId, null, null, null, null, null, UserStatus.Locked))))
            .Should().Be("USER_STATUS_USE_LOCK");

        (await CodeOf(() => UpdateHandler().HandleAsync(
                new UpdateUserCommand(_admin.UserId, null, null, null, null, null, UserStatus.Inactive))))
            .Should().Be("USER_CANNOT_DEACTIVATE_SELF");
    }

    [Fact]
    public async Task UpdateUser_parish_change_requires_the_community_to_follow()
    {
        _volunteer.CreateProfile(Guid.NewGuid());

        (await CodeOf(() => UpdateHandler(new FakeParishDirectory(allow: false)).HandleAsync(
                new UpdateUserCommand(_volunteer.UserId, null, null, null, null, Guid.NewGuid(), null))))
            .Should().Be("USER_COMMUNITY_OUTSIDE_PARISH");
        _volunteer.ParishId.Should().Be(ParishId);
    }

    [Fact]
    public async Task UpdateUser_with_stale_version_returns_version_mismatch()
    {
        (await CodeOf(() => UpdateHandler().HandleAsync(
                new UpdateUserCommand(_volunteer.UserId, _volunteer.Version + 7, null, "Ten moi", null, null, null))))
            .Should().Be("USER_VERSION_MISMATCH");
        _volunteer.FullName.Should().Be("Tinh Nguyen");
    }

    [Fact]
    public async Task UpdateUser_unknown_user_is_not_found()
    {
        (await CodeOf(() => UpdateHandler().HandleAsync(
                new UpdateUserCommand(Guid.NewGuid(), null, null, "X", null, null, null))))
            .Should().Be("USER_NOT_FOUND");
    }

    // ---- lock / unlock ----------------------------------------------------------------------

    [Fact]
    public async Task LockUser_locks_revokes_sessions_and_publishes_locked_with_reason_and_actor()
    {
        var session = new FakeTokenService().IssueRefreshToken(_volunteer.UserId);
        _refreshTokens.Seed(session.Token);

        var locked = await LockHandler().HandleAsync(new LockUserCommand(_volunteer.UserId, null, "  Vi pham noi quy  "));

        locked.Status.Should().Be(UserStatus.Locked);
        session.Token.IsRevoked.Should().BeTrue();
        var published = _events.Events.Should().ContainSingle().Which;
        published.EventType.Should().Be(IdentityUserLocked.EventType);
        var payload = (IdentityUserLocked)published.Payload!;
        payload.UserId.Should().Be(_volunteer.UserId);
        payload.ParishId.Should().Be(ParishId);
        payload.Reason.Should().Be("Vi pham noi quy");
        payload.LockedBy.Should().Be(_admin.UserId);
    }

    [Fact]
    public async Task LockUser_requires_a_reason_refuses_self_and_double_lock()
    {
        (await CodeOf(() => LockHandler().HandleAsync(new LockUserCommand(_volunteer.UserId, null, "   "))))
            .Should().Be("REASON_INVALID");
        (await CodeOf(() => LockHandler().HandleAsync(new LockUserCommand(_admin.UserId, null, "test"))))
            .Should().Be("USER_CANNOT_LOCK_SELF");

        await LockHandler().HandleAsync(new LockUserCommand(_volunteer.UserId, null, "first"));
        (await CodeOf(() => LockHandler().HandleAsync(new LockUserCommand(_volunteer.UserId, null, "again"))))
            .Should().Be("USER_ALREADY_LOCKED");
        _events.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task UnlockUser_returns_the_account_to_active_and_publishes_unlocked()
    {
        await LockHandler().HandleAsync(new LockUserCommand(_volunteer.UserId, null, "lock"));

        var unlocked = await UnlockHandler().HandleAsync(new UnlockUserCommand(_volunteer.UserId, null, "da xac minh"));

        unlocked.Status.Should().Be(UserStatus.Active);
        _events.Events.Last().EventType.Should().Be(IdentityUserUnlocked.EventType);
        ((IdentityUserUnlocked)_events.Events.Last().Payload!).UnlockedBy.Should().Be(_admin.UserId);

        (await CodeOf(() => UnlockHandler().HandleAsync(new UnlockUserCommand(_volunteer.UserId, null, "again"))))
            .Should().Be("USER_NOT_LOCKED");
    }

    // ---- assignUserRole / listAssignableRoles / listUsers -----------------------------------

    [Fact]
    public async Task AssignUserRole_replaces_the_single_role()
    {
        var updated = await RoleHandler().HandleAsync(
            new AssignUserRoleCommand(_volunteer.UserId, null, "LEADER", null));

        updated.Role.Should().Be(RoleCodes.Leader);
        updated.ParishId.Should().Be(ParishId);
    }

    [Fact]
    public async Task AssignUserRole_from_admin_to_parish_role_needs_a_parish()
    {
        var otherAdmin = TestUsers.Add(_users, _roles[RoleCodes.Admin], "admin3@example.com", null);

        (await CodeOf(() => RoleHandler().HandleAsync(
                new AssignUserRoleCommand(otherAdmin.UserId, null, RoleCodes.Leader, null))))
            .Should().Be("USER_PARISH_REQUIRED");

        var updated = await RoleHandler().HandleAsync(
            new AssignUserRoleCommand(otherAdmin.UserId, null, RoleCodes.Leader, ParishId));
        updated.ParishId.Should().Be(ParishId);
    }

    [Fact]
    public async Task AssignUserRole_refuses_own_account_and_unknown_role()
    {
        (await CodeOf(() => RoleHandler().HandleAsync(
                new AssignUserRoleCommand(_admin.UserId, null, RoleCodes.Volunteer, ParishId))))
            .Should().Be("USER_CANNOT_CHANGE_OWN_ROLE");

        (await CodeOf(() => RoleHandler().HandleAsync(
                new AssignUserRoleCommand(_volunteer.UserId, null, "OWNER", null))))
            .Should().Be("ROLE_UNKNOWN");
    }

    [Fact]
    public async Task ListAssignableRoles_returns_the_five_roles_in_canonical_order()
    {
        var roles = await new ListAssignableRolesQueryHandler(_users).HandleAsync();

        roles.Select(role => role.Code).Should().Equal(RoleCodes.All);
    }

    [Fact]
    public async Task ListUsers_pages_and_reports_total()
    {
        for (var index = 0; index < 5; index++)
        {
            TestUsers.Add(_users, _roles[RoleCodes.Volunteer], $"bulk{index}@example.com", ParishId, $"Bulk {index}");
        }

        var page = await new ListUsersQueryHandler(_users).HandleAsync(
            new UserSearchCriteria(2, 3, Search: "bulk", SortField: UserSortField.Email));

        page.TotalItems.Should().Be(5);
        page.Items.Select(user => user.Email).Should().Equal("bulk3@example.com", "bulk4@example.com");
    }
}
