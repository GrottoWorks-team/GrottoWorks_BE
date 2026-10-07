using Identity.Domain.Entities;

namespace Identity.Application.Abstractions;

public enum PasswordVerification
{
    Failed,
    Success,

    /// <summary>Correct password, but the stored hash uses outdated parameters and should be replaced.</summary>
    SuccessRehashNeeded
}

/// <summary>Password hashing abstraction. Implemented with ASP.NET Core <c>PasswordHasher&lt;T&gt;</c> (F-IDN-02).</summary>
public interface IPasswordHasher
{
    string Hash(AppUser user, string password);

    PasswordVerification Verify(AppUser user, string passwordHash, string password);

    /// <summary>
    /// Burns the same CPU time as a real verification. Called when the account does not exist so the
    /// response time does not reveal whether an email is registered.
    /// </summary>
    void VerifyDummy(string password);
}
