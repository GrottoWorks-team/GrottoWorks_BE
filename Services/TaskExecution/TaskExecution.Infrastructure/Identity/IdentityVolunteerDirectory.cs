using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TaskExecution.Application.Events;
using TaskExecution.Application.Tasks;
using TaskExecution.Application.Users;
using TaskExecution.Domain;

namespace TaskExecution.Infrastructure.Identity;

public sealed class IdentityVolunteerDirectory(HttpClient client, IConfiguration configuration, ITaskCorrelationContext correlation)
    : IVolunteerDirectory
{
    public async Task<VolunteerDirectoryPage> GetPageAsync(Guid parishId, Guid? communityId, int page, int size, CancellationToken cancellationToken)
    {
        var key = configuration["InternalApi:ApiKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32) throw Unavailable();
        try
        {
            var url = $"internal/volunteers?parishId={parishId:D}&page={page}&size={size}";
            if (communityId.HasValue) url += $"&communityId={communityId.Value:D}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Internal-Api-Key", key);
            request.Headers.Add("X-Correlation-Id", correlation.CorrelationId);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw Unavailable();
            var envelope = await response.Content.ReadFromJsonAsync<DirectoryEnvelope>(cancellationToken);
            if (envelope?.Data is null || envelope.Page is null || envelope.Page.Page != page || envelope.Page.Size != size ||
                !ValidPage(new(envelope.Data, envelope.Page.TotalItems), parishId, communityId, page, size))
                throw Unavailable();
            return new(envelope.Data, envelope.Page.TotalItems);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException or
            Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException) { throw Unavailable(); }
    }

    private static DomainRuleException Unavailable() => new("IDENTITY_SERVICE_UNAVAILABLE", "The volunteer directory is unavailable.", DomainErrorKind.ServiceUnavailable);
    private sealed record DirectoryEnvelope(IReadOnlyList<IdentityUser>? Data, DirectoryPage? Page);
    private sealed record DirectoryPage(int Page, int Size, long TotalItems);

    internal static bool ValidPage(VolunteerDirectoryPage result, Guid parishId, Guid? communityId, int page, int size) =>
        result.Items is not null && result.TotalItems >= 0 && result.Items.Count <= size &&
        (result.Items.Count == 0 || result.TotalItems >= (long)(page - 1) * size + result.Items.Count) &&
        result.Items.All(user => user is not null && user.UserId != Guid.Empty && user.ParishId == parishId &&
            (!communityId.HasValue || user.CommunityId == communityId) && user.Status == "ACTIVE" &&
            user.Role == "VOLUNTEER" && !string.IsNullOrWhiteSpace(user.DisplayName) && IdentityUserLookup.ValidSkills(user) &&
            user.Availability is not null && user.Availability.All(slot => slot is not null &&
                slot.AvailableFrom != default && slot.AvailableTo > slot.AvailableFrom));
}
