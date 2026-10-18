using FluentAssertions;
using Identity.Application.Auth.PasswordReset;
using Identity.Domain;
using Identity.Domain.Entities;
using Xunit;

namespace Identity.UnitTests.Auth;

/// <summary>F-IDN-04: requestPasswordReset / resetPassword.</summary>
public sealed class PasswordResetTests
{
    private readonly FakeUserStore _users = new();
    private readonly FakePasswordResetTokenStore _resetTokens = new();
    private readonly FakeRefreshTokenStore _refreshTokens = new();
    private readonly FakeOpaqueTokenGenerator _generator = new();
    private readonly FakePasswordResetNotifier _notifier = new();
    private readonly AppUser _user;

    public PasswordResetTests()
    {
        var roles = TestUsers.SeedRoles(_users);
        _user = TestUsers.Add(_users, roles[RoleCodes.Volunteer], "nguyen@example.com", Guid.NewGuid());
    }

    private RequestPasswordResetCommandHandler RequestHandler() =>
        new(_users, _resetTokens, _generator, _notifier, new PasswordResetSettings { TokenMinutes = 30 });

    private ResetPasswordCommandHandler ResetHandler() =>
        new(_users, _resetTokens, _refreshTokens, _generator, new FakePasswordHasher());

    [Fact]
    public async Task Request_for_an_active_account_issues_a_hashed_code_and_notifies()
    {
        await RequestHandler().HandleAsync(new RequestPasswordResetCommand(" Nguyen@Example.com "));

        var stored = _resetTokens.Tokens.Should().ContainSingle().Which;
        var sent = _notifier.Sent.Should().ContainSingle().Which;
        stored.UserId.Should().Be(_user.UserId);
        stored.TokenHash.Should().Be(_generator.Hash(sent.Token), "only the hash is persisted");
        sent.Email.Should().Be("nguyen@example.com");
        (stored.ExpiresAt - stored.CreatedAt).Should().Be(TimeSpan.FromMinutes(30));
    }

    [Theory]
    [InlineData("nobody@example.com")]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task Request_for_unknown_or_malformed_email_is_silently_accepted(string email)
    {
        await RequestHandler().HandleAsync(new RequestPasswordResetCommand(email));

        _resetTokens.Tokens.Should().BeEmpty();
        _notifier.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Request_for_a_locked_account_issues_nothing()
    {
        _user.Lock(TestUsers.Now);

        await RequestHandler().HandleAsync(new RequestPasswordResetCommand("nguyen@example.com"));

        _resetTokens.Tokens.Should().BeEmpty();
        _notifier.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_new_request_invalidates_the_previous_code()
    {
        await RequestHandler().HandleAsync(new RequestPasswordResetCommand("nguyen@example.com"));
        await RequestHandler().HandleAsync(new RequestPasswordResetCommand("nguyen@example.com"));

        _resetTokens.Tokens.Should().HaveCount(2);
        _resetTokens.Tokens[0].IsUsed.Should().BeTrue();
        _resetTokens.Tokens[1].IsUsed.Should().BeFalse();

        var first = _notifier.Sent[0].Token;
        var act = () => ResetHandler().HandleAsync(new ResetPasswordCommand(first, "NewPassword1"));
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("AUTH_RESET_TOKEN_INVALID");
    }

    [Fact]
    public async Task Reset_sets_the_password_consumes_the_code_and_revokes_refresh_tokens()
    {
        var session = new FakeTokenService().IssueRefreshToken(_user.UserId);
        _refreshTokens.Seed(session.Token);
        await RequestHandler().HandleAsync(new RequestPasswordResetCommand("nguyen@example.com"));

        await ResetHandler().HandleAsync(new ResetPasswordCommand(_notifier.Sent[0].Token, "NewPassword1"));

        _user.PasswordHash.Should().Be("hash:NewPassword1");
        _resetTokens.Tokens.Single().IsUsed.Should().BeTrue();
        session.Token.IsRevoked.Should().BeTrue();
        _refreshTokens.RevokedUsers.Should().ContainSingle()
            .Which.Should().Be((_user.UserId, RefreshToken.ReasonPasswordReset));
        _users.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Reset_twice_with_the_same_code_fails()
    {
        await RequestHandler().HandleAsync(new RequestPasswordResetCommand("nguyen@example.com"));
        var code = _notifier.Sent[0].Token;
        await ResetHandler().HandleAsync(new ResetPasswordCommand(code, "NewPassword1"));

        var act = () => ResetHandler().HandleAsync(new ResetPasswordCommand(code, "OtherPassword2"));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("AUTH_RESET_TOKEN_INVALID");
        _user.PasswordHash.Should().Be("hash:NewPassword1");
    }

    [Fact]
    public async Task Reset_with_an_expired_code_fails()
    {
        _resetTokens.Add(PasswordResetToken.Create(
            _user.UserId,
            _generator.Hash("old-code"),
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-1)));

        var act = () => ResetHandler().HandleAsync(new ResetPasswordCommand("old-code", "NewPassword1"));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("AUTH_RESET_TOKEN_INVALID");
    }

    [Fact]
    public async Task Reset_losing_a_concurrent_race_fails_without_changing_the_password()
    {
        await RequestHandler().HandleAsync(new RequestPasswordResetCommand("nguyen@example.com"));
        _resetTokens.LoseNextConsumeRace = true;

        var act = () => ResetHandler().HandleAsync(new ResetPasswordCommand(_notifier.Sent[0].Token, "NewPassword1"));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("AUTH_RESET_TOKEN_INVALID");
        _user.PasswordHash.Should().Be("hash:Volunteer123");
    }

    [Fact]
    public async Task Reset_for_an_account_locked_after_the_request_fails()
    {
        await RequestHandler().HandleAsync(new RequestPasswordResetCommand("nguyen@example.com"));
        _user.Lock(TestUsers.Now);

        var act = () => ResetHandler().HandleAsync(new ResetPasswordCommand(_notifier.Sent[0].Token, "NewPassword1"));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("AUTH_RESET_TOKEN_INVALID");
    }

    [Fact]
    public async Task Reset_enforces_the_password_policy_before_touching_the_code()
    {
        await RequestHandler().HandleAsync(new RequestPasswordResetCommand("nguyen@example.com"));

        var act = () => ResetHandler().HandleAsync(new ResetPasswordCommand(_notifier.Sent[0].Token, "short"));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("AUTH_PASSWORD_POLICY_VIOLATION");
        _resetTokens.Tokens.Single().IsUsed.Should().BeFalse();
    }

    [Fact]
    public async Task An_account_created_without_password_can_set_its_first_password()
    {
        var roles = TestUsers.SeedRoles(_users);
        var invited = TestUsers.Add(_users, roles[RoleCodes.Leader], "leader@example.com", Guid.NewGuid(), withPassword: false);

        await RequestHandler().HandleAsync(new RequestPasswordResetCommand("leader@example.com"));
        await ResetHandler().HandleAsync(new ResetPasswordCommand(_notifier.Sent[0].Token, "FirstPassword1"));

        invited.PasswordHash.Should().Be("hash:FirstPassword1");
        invited.CanAuthenticate.Should().BeTrue();
    }
}
