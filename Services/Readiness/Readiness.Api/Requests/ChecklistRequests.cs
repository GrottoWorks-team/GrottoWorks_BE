using System.ComponentModel.DataAnnotations;
using Readiness.Domain.Checklists;

namespace Readiness.Api.Requests;

/// <summary><c>ReadinessItemDefinition</c> (D-04: leader-defined, no template).</summary>
public sealed class ReadinessItemDefinitionRequest
{
    [Required(ErrorMessage = "Item name is required.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Item name must be 1–200 characters.")]
    public string? Name { get; init; }

    [StringLength(2000, ErrorMessage = "Description must not exceed 2000 characters.")]
    public string? Description { get; init; }

    /// <summary>Defaults to true (ERD <c>is_required</c> default).</summary>
    public bool? Mandatory { get; init; }

    public ItemDefinition ToDefinition() => new(Name, Description, Mandatory ?? true);
}

/// <summary><c>CreateReadinessCheck</c> (F-RDY-02).</summary>
public sealed class CreateReadinessCheckRequest
{
    [Required(ErrorMessage = "Checklist name is required.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "Checklist name must be 1–150 characters.")]
    public string? Name { get; init; }

    [Required(ErrorMessage = "Items are required.")]
    public List<ReadinessItemDefinitionRequest>? Items { get; init; }
}

/// <summary><c>ReadinessItemResult</c> (Contract 10.4): PASSED or FAILED with an optional note and evidence.</summary>
public sealed class ReadinessItemResultRequest
{
    public long? Version { get; init; }

    [Required(ErrorMessage = "Result is required.")]
    public InspectionResult? Result { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }

    public List<Guid>? EvidenceFileIds { get; init; }
}

/// <summary><c>CorrectiveTaskLinkRequest</c> (F-RDY-04): link an existing CORRECTIVE task of the work area.</summary>
public sealed class CorrectiveTaskLinkRequest
{
    public long? Version { get; init; }

    public Guid? TaskId { get; init; }
}

/// <summary><c>FileReference</c> (only the id is needed to attach an uploaded file).</summary>
public sealed class FileReferenceRequest
{
    [Required(ErrorMessage = "File id is required.")]
    public Guid? Id { get; init; }
}

/// <summary><c>VersionedCommand</c>.</summary>
public sealed class VersionedCommandRequest
{
    public long? Version { get; init; }
}

/// <summary><c>ReasonCommand</c>.</summary>
public sealed class ReasonCommandRequest
{
    public long? Version { get; init; }

    [Required(ErrorMessage = "Reason is required.")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Reason must be 1–500 characters.")]
    public string? Reason { get; init; }
}
