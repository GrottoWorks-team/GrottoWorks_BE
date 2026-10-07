using FluentAssertions;
using Identity.Application.Auth.Login;
using Identity.Application.Auth.Logout;
using Identity.Application.Auth.TokenRefresh;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;
using Xunit;

namespace Identity.UnitTests.Auth;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static AppUser SeedActiveUser(FakeUserStore users)
    {
        var role = Role.Create(RoleCodes.Volunteer, "Volunteer");
        users.SeedRole(role);
        var user = AppUser.Create("Nguyen Van A", "nguyen@example.com", null, Guid.NewGuid(), role, Now);
        user.SetPasswordHash("hash:Volunteer123", Now);
        users.Seed(user);
        return user;
    }

    private static LoginCommandHandler CreateLoginHandler(FakeUserStore users, FakeRefreshTokenStore tokens) =>
        new(users, tokens, new FakePasswordHasher(), new FakeTokenService());

    private static RefreshTokenCommandHandler CreateRefreshHandler(FakeUserStore users, FakeRefreshTokenStore tokens) =>
        new(users, tokens, new FakeTokenService());

    private static LogoutCommandHandler CreateLogoutHandler(FakeRefreshTokenStore tokens) =>
        new(tokens, new FakeTokenService());

    [Fact]
    public async Task Refresh_rotates_the_token_pair_and_links_the_family()
    {
        var users = new FakeUserStore();
        var tokens = new FakeRefreshTokenStore();
        SeedActiveUser(users);

        var first = await CreateLoginHandler(users, tokens).HandleAsync(
            new LoginCommand("nguyen@example.com", "Volunteer123"),
            CancellationToken.None);
        var familyId = tokens.AddedTokens.Single().FamilyId;

        var rotated = await CreateRefreshHandler(users, tokens).HandleAsync(
            new RefreshTokenCommand(first.RefreshToken),
            CancellationToken.None);

        rotated.RefreshToken.Should().NotBe(first.RefreshToken);

        // The old token is rotated (replaced), the new one belongs to the same family.
        var oldToken = tokens.AddedTokens.First();
        oldToken.IsRotated.Should().BeTrue();
        oldToken.ReplacedByTokenId.Should().NotBeNull();

        var newToken = tokens.AddedTokens.Last();
        newToken.FamilyId.Should().Be(familyId);
        newToken.IsReuse.Should().BeFalse();
    }

    [Fact]
    public async Task Refresh_with_an_unknown_token_fails()
    {
        var users = new FakeUserStore();
        var tokens = new FakeRefreshTokenStore();
        SeedActiveUser(users);

        var act = async () => await CreateRefreshHandler(users, tokens).HandleAsync(
            new RefreshTokenCommand("not-a-real-token"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "AUTH_INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task Refresh_detects_reuse_and_revokes_the_whole_family()
    {
        var users = new FakeUserStore();
        var tokens = new FakeRefreshTokenStore();
        SeedActiveUser(users);

        var first = await CreateLoginHandler(users, tokens).HandleAsync(
            new LoginCommand("nguyen@example.com", "Volunteer123"),
            CancellationToken.None);

        // First use rotates the token; presenting the same token again is reuse.
        var handler = CreateRefreshHandler(users, tokens);
        await handler.HandleAsync(new RefreshTokenCommand(first.RefreshToken), CancellationToken.None);

        var reuse = async () => await handler.HandleAsync(
            new RefreshTokenCommand(first.RefreshToken),
            CancellationToken.None);

        await reuse.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "AUTH_REFRESH_TOKEN_REUSED");

        tokens.AddedTokens.Should().OnlyContain(token => token.IsRevoked);
        tokens.RevokedFamilies.Should().ContainSingle();
    }

    [Fact]
    public async Task Refresh_rejects_an_expired_token()
    {
        var users = new FakeUserStore();
        var tokens = new FakeRefreshTokenStore();
        var user = SeedActiveUser(users);

        var tokenService = new FakeTokenService();
        var expiredPlaintext = "expired-token";
        var expired = RefreshToken.Create(
            user.UserId,
            Guid.NewGuid(),
            tokenService.HashRefreshToken(expiredPlaintext),
            Now.AddDays(-30),
            Now.AddDays(-1));
        tokens.Seed(expired);

        var act = async () => await new RefreshTokenCommandHandler(users, tokens, tokenService).HandleAsync(
            new RefreshTokenCommand(expiredPlaintext),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "AUTH_REFRESH_TOKEN_EXPIRED");
        expired.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_revokes_the_family_when_the_account_is_locked()
    {
        var users = new FakeUserStore();
        var tokens = new FakeRefreshTokenStore();
        var user = SeedActiveUser(users);

        var first = await CreateLoginHandler(users, tokens).HandleAsync(
            new LoginCommand("nguyen@example.com", "Volunteer123"),
            CancellationToken.None);
        user.Lock(Now);

        var act = async () => await CreateRefreshHandler(users, tokens).HandleAsync(
            new RefreshTokenCommand(first.RefreshToken),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "AUTH_ACCOUNT_LOCKED");
        tokens.AddedTokens.Should().OnlyContain(token => token.IsRevoked);
    }

    [Fact]
    public async Task Logout_revokes_the_family_and_is_idempotent()
    {
        var users = new FakeUserStore();
        var tokens = new FakeRefreshTokenStore();
        SeedActiveUser(users);

        var first = await CreateLoginHandler(users, tokens).HandleAsync(
            new LoginCommand("nguyen@example.com", "Volunteer123"),
            CancellationToken.None);

        var handler = CreateLogoutHandler(tokens);
        await handler.HandleAsync(new LogoutCommand(first.RefreshToken), CancellationToken.None);

        tokens.AddedTokens.Should().OnlyContain(token => token.IsRevoked);

        // Logging out again with the same (revoked) token or an unknown token stays a success.
        await handler.HandleAsync(new LogoutCommand(first.RefreshToken), CancellationToken.None);
        await handler.HandleAsync(new LogoutCommand("unknown-token"), CancellationToken.None);
    }
}