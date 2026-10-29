using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Reporting.Api.Requests;

/// <summary>Envelope of BuildingBlocks README §2 plus <c>eventId</c> (broker message id, inbox key).</summary>
public sealed class IntegrationEventRequest
{
    [Required(ErrorMessage = "eventId is required.")]
    public Guid? EventId { get; init; }

    [Required(ErrorMessage = "eventType is required.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "eventType must be 1–100 characters.")]
    public string? EventType { get; init; }

    public int Version { get; init; } = 1;

    public DateTimeOffset? OccurredAt { get; init; }

    [Required(ErrorMessage = "payload is required.")]
    public JsonElement? Payload { get; init; }
}
