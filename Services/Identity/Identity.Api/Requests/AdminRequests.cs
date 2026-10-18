using System.ComponentModel.DataAnnotations;
using Identity.Domain.Enums;

namespace Identity.Api.Requests;

/// <summary>F-IDN-08 <c>CreateUserRequest</c>. Omit <c>password</c> to let the owner set it via password reset.</summary>
public sealed class CreateUserRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [StringLength(255, ErrorMessage = "Email must not exceed 255 characters.")]
    public string? Email { get; init; }

    [Required(ErrorMessage = "Display name is required.")]
    [StringLength(120, ErrorMessage = "Display name must not exceed 120 characters.")]
    public string? DisplayName { get; init; }

    [StringLength(30, ErrorMessage = "Phone must not exceed 30 characters.")]
    public string? Phone { get; init; }

    [Required(ErrorMessage = "Role is required.")]
    [StringLength(50, ErrorMessage = "Role must not exceed 50 characters.")]
    public string? Role { get; init; }

    /// <summary>BR-67: required for every role except ADMIN.</summary>
    public Guid? ParishId { get; init; }

    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be 8-128 characters.")]
    public string? Password { get; init; }
}

/// <summary>F-IDN-08 <c>UpdateUserRequest</c> — PATCH semantics; status accepts ACTIVE or INACTIVE.</summary>
public sealed class UpdateUserRequest
{
    /// <summary>Current <c>version</c> of the user (optimistic concurrency); stale → 412.</summary>
    public long? Version { get; init; }

    [StringLength(255, ErrorMessage = "Email must not exceed 255 characters.")]
    public string? Email { get; init; }

    [StringLength(120, ErrorMessage = "Display name must not exceed 120 characters.")]
    public string? DisplayName { get; init; }

    [StringLength(30, ErrorMessage = "Phone must not exceed 30 characters.")]
    public string? Phone { get; init; }

    public Guid? ParishId { get; init; }

    public UserStatus? Status { get; init; }
}

/// <summary><c>ReasonCommand</c> (API Contract 6, Common): reason 1–500 characters.</summary>
public sealed class ReasonCommandRequest
{
    public long? Version { get; init; }

    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Reason must be 1-500 characters.")]
    public string? Reason { get; init; }
}

/// <summary>F-IDN-08 <c>RoleAssignmentRequest</c>: the single role, plus the parish when the role needs one.</summary>
public sealed class RoleAssignmentRequest
{
    public long? Version { get; init; }

    [Required(ErrorMessage = "Role is required.")]
    [StringLength(50, ErrorMessage = "Role must not exceed 50 characters.")]
    public string? Role { get; init; }

    public Guid? ParishId { get; init; }
}
