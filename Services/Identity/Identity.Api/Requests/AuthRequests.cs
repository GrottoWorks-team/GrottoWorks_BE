using System.ComponentModel.DataAnnotations;

namespace Identity.Api.Requests;

/// <summary>F-IDN-02: public self-registration (always creates a VOLUNTEER account — BR-67 requires a parish).</summary>
public sealed class RegisterRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [StringLength(255, ErrorMessage = "Email must not exceed 255 characters.")]
    public string? Email { get; init; }

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be 8-128 characters.")]
    public string? Password { get; init; }

    [Required(ErrorMessage = "Display name is required.")]
    [StringLength(120, ErrorMessage = "Display name must not exceed 120 characters.")]
    public string? DisplayName { get; init; }

    [StringLength(30, ErrorMessage = "Phone must not exceed 30 characters.")]
    public string? Phone { get; init; }

    [Required(ErrorMessage = "Parish is required.")]
    public Guid? ParishId { get; init; }
}

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Email is required.")]
    public string? Email { get; init; }

    [Required(ErrorMessage = "Password is required.")]
    public string? Password { get; init; }
}

public sealed class RefreshTokenRequest
{
    [Required(ErrorMessage = "Refresh token is required.")]
    public string? RefreshToken { get; init; }
}

public sealed class LogoutRequest
{
    [Required(ErrorMessage = "Refresh token is required.")]
    public string? RefreshToken { get; init; }
}

/// <summary>F-IDN-04 <c>ForgotPasswordRequest</c>.</summary>
public sealed class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [StringLength(255, ErrorMessage = "Email must not exceed 255 characters.")]
    public string? Email { get; init; }
}

/// <summary>F-IDN-04 <c>ResetPasswordRequest</c>: the code received out of band plus the new password.</summary>
public sealed class ResetPasswordRequest
{
    [Required(ErrorMessage = "Reset token is required.")]
    [StringLength(200, ErrorMessage = "Reset token must not exceed 200 characters.")]
    public string? Token { get; init; }

    [Required(ErrorMessage = "New password is required.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be 8-128 characters.")]
    public string? NewPassword { get; init; }
}
