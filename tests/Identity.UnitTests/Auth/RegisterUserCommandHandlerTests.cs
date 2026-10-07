using FluentAssertions;
using Identity.Application.Auth.RegisterUser;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;
using Xunit;

namespace Identity.UnitTests.Auth;

public sealed class RegisterUserCommandHandlerTests
{
    private static readonly Guid ParishId = Guid.NewGuid();

    [Fact]
    public async Task Register_creates_active_volunteer_and_publishes_event()
    {
        var stores = TestFactory.CreateEmptyStores();
        stores.Users.SeedRole(Role.Create(RoleCodes.Volunteer, "Volunteer"));

        var handler = new RegisterUserCommandHandler(
            stores.Users,
            new FakePasswordHasher(),
            stores.Events);

        var user = await handler.HandleAsync(
            new RegisterUserCommand(
                "  Nguyen Van A  ",
                "NGUYEN@EXAMPLE.COM",
                "Volunteer123",
                "0901234567",
                ParishId),
            CancellationToken.None);

        user.Email.Should().Be("nguyen@example.com");
        user.DisplayName.Should().Be("Nguyen Van A");
        user.Role.Should().Be(RoleCodes.Volunteer);
        user.Status.Should().Be(UserStatus.Active);
        user.ParishId.Should().Be(ParishId);

        stores.Users.SaveChangesCount.Should().Be(1);
        var published = stores.Events.Events.Should().ContainSingle().Subject;
        published.EventType.Should().Be("identity.user.registered");
        published.Version.Should().Be(1);
        published.Payload.Should().BeOfType<Identity.Contracts.Events.IdentityUserRegistered>();
    }

    [Fact]
    public async Task Register_rejects_duplicate_email()
    {
        var stores = TestFactory.CreateEmptyStores();
        stores.Users.SeedRole(Role.Create(RoleCodes.Volunteer, "Volunteer"));

        var handler = new RegisterUserCommandHandler(
            stores.Users,
            new FakePasswordHasher(),
            stores.Events);
        await handler.HandleAsync(
            new RegisterUserCommand("Nguyen Van A", "nguyen@example.com", "Volunteer123", null, ParishId),
            CancellationToken.None);

        var act = async () => await handler.HandleAsync(
            new RegisterUserCommand("Nguyen Van B", "NGUYEN@EXAMPLE.COM", "Volunteer123", null, ParishId),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "AUTH_EMAIL_ALREADY_EXISTS");
    }

    [Fact]
    public async Task Register_requires_volunteer_role_to_exist()
    {
        var stores = TestFactory.CreateEmptyStores();

        var handler = new RegisterUserCommandHandler(
            stores.Users,
            new FakePasswordHasher(),
            stores.Events);

        var act = async () => await handler.HandleAsync(
            new RegisterUserCommand("Nguyen Van A", "nguyen@example.com", "Volunteer123", null, ParishId),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("no digits here")]
    [InlineData("123456789")]
    [InlineData("")]
    public async Task Register_rejects_password_that_violates_policy(string password)
    {
        var stores = TestFactory.CreateEmptyStores();
        stores.Users.SeedRole(Role.Create(RoleCodes.Volunteer, "Volunteer"));

        var handler = new RegisterUserCommandHandler(
            stores.Users,
            new FakePasswordHasher(),
            stores.Events);

        var act = async () => await handler.HandleAsync(
            new RegisterUserCommand("Nguyen Van A", "nguyen@example.com", password, null, ParishId),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "AUTH_PASSWORD_POLICY_VIOLATION");
        stores.Users.AddedUsers.Should().BeEmpty();
    }
}