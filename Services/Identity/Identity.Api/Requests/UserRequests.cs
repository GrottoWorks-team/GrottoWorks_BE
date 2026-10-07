using System.ComponentModel.DataAnnotations;

namespace Identity.Api.Requests;

/// <summary>F-IDN-05: PATCH semantics — every field is optional and <c>null</c> means "unchanged".</summary>
public sealed class UpdateProfileRequest
{
    [StringLength(120, ErrorMessage = "Display name must not exceed 120 characters.")]
    public string? DisplayName { get; init; }

    [StringLength(30, ErrorMessage = "Phone must not exceed 30 characters.")]
    public string? Phone { get; init; }

    [StringLength(1000, ErrorMessage = "Introduction must not exceed 1000 characters.")]
    public string? Introduction { get; init; }

    public Guid? CommunityId { get; init; }
}
