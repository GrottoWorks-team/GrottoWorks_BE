using System.ComponentModel.DataAnnotations;

namespace Identity.Api.Requests;

/// <summary>F-IDN-05: PATCH semantics — every field is optional and <c>null</c> means "unchanged".</summary>
public sealed class UpdateProfileRequest
{
    [StringLength(120, ErrorMessage = "Display name must not exceed 120 characters.")]
    public string? DisplayName { get; init; }

    [StringLength(30, ErrorMessage = "Phone must not exceed 30 characters.")]
    public string? Phone { get; init; }

    [StringLength(2000, ErrorMessage = "Introduction must not exceed 2000 characters.")]
    public string? Introduction { get; init; }

    public Guid? CommunityId { get; init; }
}

/// <summary>F-IDN-07 <c>SkillIdList</c>: the complete set of declared skills (empty clears them).</summary>
public sealed class ReplaceSkillsRequest : IValidatableObject
{
    [MaxLength(50, ErrorMessage = "At most 50 skills can be declared.")]
    public List<Guid>? SkillIds { get; init; }

    [MaxLength(50)]
    public List<DeclaredSkillRequest>? Skills { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((SkillIds is null) == (Skills is null))
            yield return new ValidationResult("Provide either skillIds or skills, not both (an empty array clears all skills).");
    }
}

public sealed record DeclaredSkillRequest(Guid SkillId, [property: StringLength(30)] string? SkillLevel);

/// <summary>F-IDN-07 <c>AvailabilitySlot</c>: UTC date-time range, <c>availableTo</c> after <c>availableFrom</c>.</summary>
public sealed class AvailabilitySlotRequest
{
    [Required(ErrorMessage = "availableFrom is required.")]
    public DateTimeOffset? AvailableFrom { get; init; }

    [Required(ErrorMessage = "availableTo is required.")]
    public DateTimeOffset? AvailableTo { get; init; }

    [StringLength(500, ErrorMessage = "Note must not exceed 500 characters.")]
    public string? Note { get; init; }
}
