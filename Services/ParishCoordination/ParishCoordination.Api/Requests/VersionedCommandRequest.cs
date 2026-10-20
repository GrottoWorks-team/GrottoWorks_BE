using System.ComponentModel.DataAnnotations;

namespace ParishCoordination.Api.Requests;

public sealed class VersionedCommandRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Version must be a positive integer.")]
    public int? Version { get; init; }
}
