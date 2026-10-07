using System.ComponentModel.DataAnnotations;

namespace ParishCoordination.Api.Requests;

public sealed class CreateParishRequest
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(150, ErrorMessage = "Name must not exceed 150 characters.")]
    public string? Name { get; init; }

    [StringLength(255, ErrorMessage = "Address must not exceed 255 characters.")]
    public string? Address { get; init; }

    public string? Description { get; init; }
}
