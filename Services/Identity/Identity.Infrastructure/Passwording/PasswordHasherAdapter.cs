using Identity.Application.Abstractions;
using Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Identity.Infrastructure.Passwording;

/// <summary>Adapter over ASP.NET Core <c>PasswordHasher&lt;AppUser&gt;</c> (F-IDN-02).</summary>
public sealed class PasswordHasherAdapter : IPasswordHasher
{
    private readonly PasswordHasher<AppUser> _passwordHasher = new();

    // The default PasswordHasher ignores the user argument, so a null user is safe here.
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<AppUser>().HashPassword(null!, Guid.NewGuid().ToString("N")));

    public string Hash(AppUser user, string password) =>
        _passwordHasher.HashPassword(user, password);

    public PasswordVerification Verify(AppUser user, string passwordHash, string password) =>
        _passwordHasher.VerifyHashedPassword(user, passwordHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordVerification.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
            _ => PasswordVerification.Failed
        };

    public void VerifyDummy(string password) =>
        _passwordHasher.VerifyHashedPassword(null!, DummyHash.Value, password ?? string.Empty);
}
