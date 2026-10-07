using Identity.Application.Abstractions;
using Identity.Application.Accounts;
using Identity.Contracts.Events;
using Identity.Domain;
using Identity.Domain.Entities;

namespace Identity.Application.Auth.RegisterUser;

public sealed record RegisterUserCommand(
    string FullName,
    string Email,
    string Password,
    string? Phone,
    Guid ParishId);

/// <summary>F-IDN-02: public self-registration. Always creates an ACTIVE VOLUNTEER account (one role per account).</summary>
public sealed class RegisterUserCommandHandler(
    IUserAccountStore userAccountStore,
    IPasswordHasher passwordHasher,
    IIntegrationEventPublisher eventPublisher)
{
    public async Task<UserDto> HandleAsync(
        RegisterUserCommand command,
        CancellationToken cancellationToken = default)
    {
        PasswordPolicy.Validate(command.Password);

        var email = AppUser.NormalizeEmail(command.Email);

        if (await userAccountStore.EmailExistsAsync(email, cancellationToken))
        {
            throw new DomainException(
                "AUTH_EMAIL_ALREADY_EXISTS",
                "An account with this email already exists.");
        }

        var volunteerRole = await userAccountStore.GetRoleByCodeAsync(RoleCodes.Volunteer, cancellationToken)
            ?? throw new InvalidOperationException(
                "The VOLUNTEER role is missing. Run the Identity data seeder (F-IDN-01).");

        var now = DateTimeOffset.UtcNow;
        var user = AppUser.Create(
            command.FullName,
            email,
            command.Phone,
            command.ParishId,
            volunteerRole,
            now);

        user.SetPasswordHash(passwordHasher.Hash(user, command.Password), now);

        userAccountStore.Add(user);
        await userAccountStore.SaveChangesAsync(cancellationToken);

        await eventPublisher.PublishAsync(
            IdentityUserRegistered.EventType,
            IdentityUserRegistered.Version,
            new IdentityUserRegistered(
                user.UserId,
                user.Email,
                user.FullName,
                user.ParishId!.Value,
                volunteerRole.Code,
                now),
            "user",
            user.UserId,
            cancellationToken);

        return UserDto.From(user);
    }
}
