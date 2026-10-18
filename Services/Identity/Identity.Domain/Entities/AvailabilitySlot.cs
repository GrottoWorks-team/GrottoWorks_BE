namespace Identity.Domain.Entities;

/// <summary>Input value for <see cref="AppUser.ReplaceAvailability"/> (API Contract <c>AvailabilitySlot</c>).</summary>
public sealed record AvailabilitySlot(DateTimeOffset AvailableFrom, DateTimeOffset AvailableTo, string? Note);
