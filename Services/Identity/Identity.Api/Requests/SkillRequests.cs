using System.ComponentModel.DataAnnotations;
using Identity.Domain.Enums;

namespace Identity.Api.Requests;

/// <summary>F-IDN-06: ADMIN only. The code is unique and normalized to upper case.</summary>
public sealed class CreateSkillRequest
{
    [Required(ErrorMessage = "Code is required.")]
    [StringLength(50, ErrorMessage = "Code must not exceed 50 characters.")]
    public string? Code { get; init; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100, ErrorMessage = "Name must not exceed 100 characters.")]
    public string? Name { get; init; }

    [StringLength(500, ErrorMessage = "Description must not exceed 500 characters.")]
    public string? Description { get; init; }
}

/// <summary>PATCH semantics: omitted/null fields are left unchanged; status INACTIVE deactivates (never deletes).</summary>
public sealed class UpdateSkillRequest
{
    [StringLength(100, ErrorMessage = "Name must not exceed 100 characters.")]
    public string? Name { get; init; }

    [StringLength(500, ErrorMessage = "Description must not exceed 500 characters.")]
    public string? Description { get; init; }

    public SkillStatus? Status { get; init; }
}
