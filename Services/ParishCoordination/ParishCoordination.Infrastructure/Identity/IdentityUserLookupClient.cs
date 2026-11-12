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
    : IIdentityUserLookup, IIdentityUserBatchLookup
{
    /// <summary>Matches <c>GetInternalUsersQueryHandler.MaxIds</c> on the Identity service.</summary>
    private const int MaxIdsPerRequest = 100;

    private const string InternalApiKeyHeader = "X-Internal-Api-Key";
    private const string CorrelationIdHeader = "X-Correlation-Id";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<Guid, IdentityUser>> GetByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, IdentityUser>();
        var distinct = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0)
        {
            return result;
        }

        if (options.InternalApiKey.Length < IdentityClientOptions.MinimumApiKeyLength)
        {
            throw new IdentityUserLookupUnavailableException(
                $"Configuration '{IdentityClientOptions.InternalApiKeyConfigurationKey}' is missing or too short.");
        }

        foreach (var chunk in distinct.Chunk(MaxIdsPerRequest))
        {
            var users = await SendLookupAsync(chunk, cancellationToken);
            foreach (var user in users)
            {
                result[user.UserId] = user;
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<IdentityUser>> SendLookupAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var requested = userIds.ToHashSet();

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"internal/users?ids={string.Join(',', requested.Select(id => id.ToString("D")))}");

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
                    "Identity user batch lookup returned HTTP {StatusCode}.",
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

            if (envelope.Data.Any(user => !requested.Contains(user.UserId)))
            {
                throw new IdentityUserLookupUnavailableException(
                    "Identity user lookup returned unexpected user data.");
            }

            var users = new List<IdentityUser>(envelope.Data.Count);
            foreach (var user in envelope.Data)
            {
                if (!Enum.TryParse<IdentityUserStatus>(
                        user.Status,
                        ignoreCase: true,
                        out var status))
                {
                    throw new IdentityUserLookupUnavailableException(
                        "Identity user lookup returned an unknown user status.");
                }

                users.Add(new IdentityUser(
                    user.UserId,
                    status,
                    user.Role,
                    user.ParishId));
            }

            return users;
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
            logger.LogWarning(exception, "Identity user batch lookup failed.");
            throw new IdentityUserLookupUnavailableException(
                "Identity user lookup is unavailable.",
                exception);
        }
    }

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
