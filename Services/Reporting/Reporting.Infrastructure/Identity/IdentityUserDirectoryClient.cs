using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Reporting.Application.Users;

namespace Reporting.Infrastructure.Identity;

/// <summary>Where Identity lives and the shared key for <c>/internal/**</c> (same keys as Resource).</summary>
public sealed record IdentityClientOptions(Uri BaseAddress, string InternalApiKey)
{
    public const string BaseAddressConfigurationKey = "IdentityClient:BaseAddress";
    public const string InternalApiKeyConfigurationKey = "InternalApi:ApiKey";
    public const int MinimumApiKeyLength = 32;
}

/// <summary>Typed client for Identity <c>GET /internal/users?ids=</c> (at most 100 ids per call), forwarding the correlation id.</summary>
public sealed class IdentityUserDirectoryClient(
    HttpClient httpClient,
    IdentityClientOptions options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<IdentityUserDirectoryClient> logger)
    : IUserDirectory
{
    private const int BatchSize = 100;
    private const string InternalApiKeyHeader = "X-Internal-Api-Key";
    private const string CorrelationIdHeader = "X-Correlation-Id";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var names = new Dictionary<Guid, string>();
        if (userIds.Count == 0 || options.InternalApiKey.Length < IdentityClientOptions.MinimumApiKeyLength)
        {
            return names;
        }

        try
        {
            foreach (var batch in userIds.Distinct().Chunk(BatchSize))
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"internal/users?ids={string.Join(',', batch.Select(id => id.ToString("D")))}");
                request.Headers.TryAddWithoutValidation(InternalApiKeyHeader, options.InternalApiKey);
                var correlationId = httpContextAccessor.HttpContext?.Request.Headers[CorrelationIdHeader].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(correlationId))
                {
                    request.Headers.TryAddWithoutValidation(CorrelationIdHeader, correlationId);
                }

                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Identity user lookup returned HTTP {StatusCode}; names are left out.", (int)response.StatusCode);
                    return names;
                }

                var envelope = await response.Content.ReadFromJsonAsync<Envelope>(JsonOptions, cancellationToken);
                foreach (var user in envelope?.Data ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(user.DisplayName))
                    {
                        names[user.UserId] = user.DisplayName;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Identity user lookup failed; names are left out.");
        }

        return names;
    }

    private sealed record Envelope(IReadOnlyList<UserResponse> Data);

    private sealed record UserResponse(Guid UserId, string? DisplayName);
}
