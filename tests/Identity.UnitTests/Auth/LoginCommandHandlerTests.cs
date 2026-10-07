using FluentAssertions;
using Identity.Application.Auth.Login;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;
using Xunit;

namespace Identity.UnitTests.Auth;

public sealed class LoginCommandHandlerTests
{
    private static readonly Guid ParishId = Guid.NewGuid();

    private record SeededAccount(FakeUserStore Users, FakeRefreshTokenStore Tokens, AppUser User);

    private static SeededAccount SeedActiveUser(string password = "Volunteer123")
    {
        var users = new FakeUserStore();
        var tokens = new FakeRefreshTokenStore();
        var role = Role.Create(RoleCodes.Volunteer, "Volunteer");
        users.SeedRole(role);

        var user = AppUser.Create("Nguyen Van A", "nguyen@example.com", null, ParishId, role, DateTimeOffset.UtcNow);
        user.SetPasswordHash(new FakePasswordHasher().Hash(user, password), DateTimeOffset.UtcNow);
        users.Seed(user);

        return new SeededAccount(users, tokens, user);
    }

    private static LoginCommandHandler CreateHandler(SeededAccount account) =>
        new(account.Users, account.Tokens, new FakePasswordHasher(), new FakeTokenService());

    [Fact]
    public async Task Login_issues_a_token_pair_when_credentials_are_valid()
    {
        var account = SeedActiveUser();
        var handler = CreateHandler(account);

        var result = await handler.HandleAsync(
            new LoginCommand("NGUYEN@EXAMPLE.COM", "Volunteer123"),
            CancellationToken.None);

        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.TokenType.Should().Be("Bearer");
        result.ExpiresIn.Should().BeGreaterThan(0);
        account.Tokens.AddedTokens.Should().ContainSingle();
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_invalid_credentials()
    {
        var account = SeedActiveUser();
        var handler = CreateHandler(account);

        var act = async () => await handler.HandleAsync(
            new LoginCommand("nguyen@example.com", "WrongPass123"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "AUTH_INVALID_CREDENTIALS");
        account.Tokens.AddedTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Login_with_unknown_email_discloses_nothing()
    {
        var account = SeedActiveUser();
        var handler = CreateHandler(account);

        var act = async () => await handler.HandleAsync(
            new LoginCommand("nobody@example.com", "Whatever123"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "AUTH_INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Login_with_locked_account_fails_even_with_correct_password()
    {
        var account = SeedActiveUser();
        account.User.Lock(DateTimeOffset.UtcNow);
        var handler = CreateHandler(account);

        var act = async () => await handler.HandleAsync(
            new LoginCommand("nguyen@example.com", "Volunteer123"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "AUTH_ACCOUNT_LOCKED");
        account.Tokens.AddedTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Login_with_inactive_account_fails_even_with_correct_password()
    {
        var account = SeedActiveUser();
        account.User.Deactivate(DateTimeOffset.UtcNow);
        var handler = CreateHandler(account);

        var act = async () => await handler.HandleAsync(
            new LoginCommand("nguyen@example.com", "Volunteer123"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "AUTH_ACCOUNT_INACTIVE");
        account.Tokens.AddedTokens.Should().BeEmpty();
    }
}