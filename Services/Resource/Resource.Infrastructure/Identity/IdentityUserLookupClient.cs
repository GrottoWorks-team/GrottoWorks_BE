using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Resource.Application.Users;

namespace Resource.Infrastructure.Identity;

/// <summary>Typed client for Identity <c>GET /internal/users?ids=</c> (F-IDN-09), forwarding the correlation id.</summary>
public sealed class IdentityUserLookupClient(
    HttpClient httpClient,
    IdentityClientOptions options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<IdentityUserLookupClient> logger)
    : IIdentityUserLookup
{
    private const string InternalApiKeyHeader = "X-Internal-Api-Key";
    private const string CorrelationIdHeader = "X-Correlation-Id";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IdentityUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (options.InternalApiKey.Length < IdentityClientOptions.MinimumApiKeyLength)
        {
            throw new IdentityUserLookupUnavailableException(
                $"Configuration '{IdentityClientOptions.InternalApiKeyConfigurationKey}' is missing or too short.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"internal/users?ids={userId:D}");
            request.Headers.TryAddWithoutValidation(InternalApiKeyHeader, options.InternalApiKey);
            var correlationId = httpContextAccessor.HttpContext?.Request.Headers[CorrelationIdHeader].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                request.Headers.TryAddWithoutValidation(CorrelationIdHeader, correlationId);
            }

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Identity user lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
                throw new IdentityUserLookupUnavailableException("Identity user lookup returned an unsuccessful response.");
            }

            var envelope = await response.Content.ReadFromJsonAsync<Envelope>(JsonOptions, cancellationToken);
            if (envelope?.Data is null)
            {
                throw new IdentityUserLookupUnavailableException("Identity user lookup returned an invalid response body.");
            }

            return envelope.Data.Count switch
            {
                0 => null,
                1 when envelope.Data[0].UserId == userId => envelope.Data[0] is var user
                    ? new IdentityUser(user.UserId, user.DisplayName ?? string.Empty, user.Status ?? string.Empty, user.Role ?? string.Empty, user.ParishId)
                    : null,
                _ => throw new IdentityUserLookupUnavailableException("Identity user lookup returned unexpected user data.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IdentityUserLookupUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Identity user lookup failed.");
            throw new IdentityUserLookupUnavailableException("Identity user lookup is unavailable.", exception);
        }
    }

    private sealed record Envelope(IReadOnlyList<UserResponse> Data);

    private sealed record UserResponse(Guid UserId, string? DisplayName, string? Status, string? Role, Guid? ParishId);
}
