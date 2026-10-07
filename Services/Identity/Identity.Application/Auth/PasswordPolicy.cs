using Identity.Domain;

namespace Identity.Application.Auth;

/// <summary>Password policy for self-registration (F-IDN-02).</summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static void Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) ||
            password.Length < MinLength ||
            password.Length > MaxLength ||
            !password.Any(char.IsLetter) ||
            !password.Any(char.IsDigit))
        {
            throw new DomainException(
                "AUTH_PASSWORD_POLICY_VIOLATION",
                $"Password must be {MinLength}-{MaxLength} characters and contain at least one letter and one digit.");
        }
    }
}
