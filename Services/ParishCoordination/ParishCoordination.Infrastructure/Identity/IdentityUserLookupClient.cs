using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ParishCoordination.Application.Users;

namespace ParishCoordination.Infrastructure.Identity;

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

    public async Task<IdentityUser?> GetByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (options.InternalApiKey.Length < IdentityClientOptions.MinimumApiKeyLength)
        {
            throw new IdentityUserLookupUnavailableException(
                $"Configuration '{IdentityClientOptions.InternalApiKeyConfigurationKey}' is missing or too short.");
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"internal/users?ids={userId:D}");

            request.Headers.TryAddWithoutValidation(
                InternalApiKeyHeader,
                options.InternalApiKey);

            var correlationId = httpContextAccessor.HttpContext?
                .Request.Headers[CorrelationIdHeader]
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                request.Headers.TryAddWithoutValidation(
                    CorrelationIdHeader,
                    correlationId);
            }

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Identity user lookup returned HTTP {StatusCode}.",
                    (int)response.StatusCode);

                throw new IdentityUserLookupUnavailableException(
                    "Identity user lookup returned an unsuccessful response.");
            }

            var envelope = await response.Content.ReadFromJsonAsync<IdentityUserEnvelope>(
                JsonOptions,
                cancellationToken);

            if (envelope?.Data is null)
            {
                throw new IdentityUserLookupUnavailableException(
                    "Identity user lookup returned an invalid response body.");
            }

            if (envelope.Data.Count == 0)
            {
                return null;
            }

            if (envelope.Data.Count != 1 || envelope.Data[0].UserId != userId)
            {
                throw new IdentityUserLookupUnavailableException(
                    "Identity user lookup returned unexpected user data.");
            }

            var user = envelope.Data[0];
            if (!Enum.TryParse<IdentityUserStatus>(
                    user.Status,
                    ignoreCase: true,
                    out var status))
            {
                throw new IdentityUserLookupUnavailableException(
                    "Identity user lookup returned an unknown user status.");
            }

            return new IdentityUser(
                user.UserId,
                status,
                user.Role,
                user.ParishId);
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
            throw new IdentityUserLookupUnavailableException(
                "Identity user lookup is unavailable.",
                exception);
        }
    }

    private sealed record IdentityUserEnvelope(IReadOnlyList<IdentityUserResponse> Data);

    private sealed record IdentityUserResponse(
        Guid UserId,
        string Status,
        string Role,
        Guid? ParishId);
}
