using Identity.Domain.Entities;

namespace Identity.Application.Abstractions;

/// <summary>Password hashing abstraction. Implemented with ASP.NET Core <c>PasswordHasher&lt;T&gt;</c> (F-IDN-02).</summary>
public interface IPasswordHasher
{
    string Hash(AppUser user, string password);

    bool Verify(AppUser user, string passwordHash, string password);
}
