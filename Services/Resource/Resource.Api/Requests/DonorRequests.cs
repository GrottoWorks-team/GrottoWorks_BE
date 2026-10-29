using System.ComponentModel.DataAnnotations;

namespace Resource.Api.Requests;

/// <summary><c>CreateDonorRequest</c> (F-RES-12). <c>parishId</c> is only for ADMIN; an MO always records for their parish.</summary>
public sealed class CreateDonorRequest
{
    [Required(ErrorMessage = "Donor name is required.")]
    [StringLength(150, ErrorMessage = "Donor name must not exceed 150 characters.")]
    public string? DonorName { get; init; }

    [StringLength(30, ErrorMessage = "Phone must not exceed 30 characters.")]
    public string? Phone { get; init; }

    [StringLength(255, ErrorMessage = "Email must not exceed 255 characters.")]
    public string? Email { get; init; }

    [StringLength(255, ErrorMessage = "Address must not exceed 255 characters.")]
    public string? Address { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }

    /// <summary>Optional verified account link (unique).</summary>
    public Guid? UserId { get; init; }

    public Guid? ParishId { get; init; }
}

/// <summary><c>UpdateDonorRequest</c> — PATCH of name and contact details; an empty optional field clears it.</summary>
public sealed class UpdateDonorRequest
{
    public long? Version { get; init; }

    [StringLength(150, ErrorMessage = "Donor name must not exceed 150 characters.")]
    public string? DonorName { get; init; }

    [StringLength(30, ErrorMessage = "Phone must not exceed 30 characters.")]
    public string? Phone { get; init; }

    [StringLength(255, ErrorMessage = "Email must not exceed 255 characters.")]
    public string? Email { get; init; }

    [StringLength(255, ErrorMessage = "Address must not exceed 255 characters.")]
    public string? Address { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}
