using Identity.Application.Abstractions;
using Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Identity.Infrastructure.Passwording;

/// <summary>Adapter over ASP.NET Core <c>PasswordHasher&lt;AppUser&gt;</c> (F-IDN-02).</summary>
public sealed class PasswordHasherAdapter : IPasswordHasher
{
    private readonly PasswordHasher<AppUser> _passwordHasher = new();

    public string Hash(AppUser user, string password) =>
        _passwordHasher.HashPassword(user, password);

    public bool Verify(AppUser user, string passwordHash, string password) =>
        _passwordHasher.VerifyHashedPassword(user, passwordHash, password) ==
        PasswordVerificationResult.Success;
}
