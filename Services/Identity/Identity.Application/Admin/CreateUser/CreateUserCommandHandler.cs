using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Application.Auth;
using Identity.Contracts.Events;
using Identity.Domain;
using Identity.Domain.Entities;

namespace Identity.Application.Admin.CreateUser;

/// <summary>
/// <paramref name="Password"/> is optional: without it the account has no password and the owner sets
/// one through <c>requestPasswordReset</c> → <c>resetPassword</c> (invitation flow).
/// </summary>
public sealed record CreateUserCommand(
    string Email,
    string DisplayName,
    string? Phone,
    string Role,
    Guid? ParishId,
    string? Password);

/// <summary>
/// F-IDN-08 <c>createUser</c> (ADMIN): creates an ACTIVE account with exactly one role.
/// BR-67: every role except ADMIN requires a parish. Publishes <c>identity.user.registered</c> for
/// parish accounts (the v1 payload requires a parish, so ADMIN accounts publish nothing).
/// </summary>
public sealed class CreateUserCommandHandler(
    IUserAccountStore userAccountStore,
    IParishDirectory parishDirectory,
    IPasswordHasher passwordHasher,
    IIntegrationEventPublisher eventPublisher)
{
    public async Task<AdminUserDto> HandleAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Password is not null)
        {
            PasswordPolicy.Validate(command.Password);
        }

        var role = await AdminGuards.LoadRoleAsync(userAccountStore, command.Role, cancellationToken);
        var email = AppUser.NormalizeEmail(command.Email);

        if (await userAccountStore.EmailExistsAsync(email, cancellationToken))
        {
            throw new DomainException("AUTH_EMAIL_ALREADY_EXISTS", "An account with this email already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var user = AppUser.Create(command.DisplayName, email, command.Phone, command.ParishId, role, now);

        await AdminGuards.EnsureParishExistsAsync(parishDirectory, command.ParishId, cancellationToken);

        if (command.Password is not null)
        {
            user.SetPasswordHash(passwordHasher.Hash(user, command.Password), now);
        }

        userAccountStore.Add(user);
        await userAccountStore.SaveChangesAsync(cancellationToken);

        if (user.ParishId is { } parishId)
        {
            await eventPublisher.PublishAsync(
                IdentityUserRegistered.EventType,
                IdentityUserRegistered.Version,
                new IdentityUserRegistered(user.UserId, user.Email, user.FullName, parishId, role.Code, now),
                "user",
                user.UserId,
                cancellationToken);
        }

        return AdminUserDto.From(user);
    }
}
