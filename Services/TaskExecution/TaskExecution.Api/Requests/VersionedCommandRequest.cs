using System.ComponentModel.DataAnnotations;

namespace TaskExecution.Api.Requests;

public sealed class VersionedCommandRequest
{
    [Range(1, int.MaxValue)]
    public int? Version { get; init; }
}
