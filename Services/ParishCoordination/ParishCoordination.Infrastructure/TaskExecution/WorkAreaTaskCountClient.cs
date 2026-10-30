using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ParishCoordination.Application.WorkAreas;

namespace ParishCoordination.Infrastructure.TaskExecution;

public sealed class WorkAreaTaskCountClient(
    HttpClient httpClient,
    TaskExecutionClientOptions options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<WorkAreaTaskCountClient> logger)
    : IWorkAreaTaskCountClient
{
    private const string InternalApiKeyHeader = "X-Internal-Api-Key";
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WorkAreaTaskCounts> GetAsync(
        Guid workAreaId,
        CancellationToken cancellationToken = default)
    {
        if (options.InternalApiKey.Length < TaskExecutionClientOptions.MinimumApiKeyLength)
        {
            throw new WorkAreaTaskCountUnavailableException(
                $"Configuration '{TaskExecutionClientOptions.InternalApiKeyConfigurationKey}' is missing or too short.");
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"internal/work-area-task-counts?workAreaIds={workAreaId:D}");

            request.Headers.TryAddWithoutValidation(
                InternalApiKeyHeader,
                options.InternalApiKey);
            ForwardCorrelationId(request);

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "TaskExecution work area count query returned HTTP {StatusCode}.",
                    (int)response.StatusCode);

                throw new WorkAreaTaskCountUnavailableException(
                    "TaskExecution returned an unsuccessful response.");
            }

            var envelope = await response.Content.ReadFromJsonAsync<CountEnvelope>(
                JsonOptions,
                cancellationToken);

            if (envelope?.Data is null ||
                envelope.Data.Count != 1 ||
                envelope.Data[0].WorkAreaId != workAreaId)
            {
                throw new WorkAreaTaskCountUnavailableException(
                    "TaskExecution returned unexpected work area count data.");
            }

            var data = envelope.Data[0];
            EnsureValidCounts(data);

            return new WorkAreaTaskCounts(
                data.WorkAreaId,
                data.TotalTasks,
                data.CancelledTasks,
                data.EligibleTasks,
                data.CompletedTasks,
                data.OverdueTasks,
                data.CalculatedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WorkAreaTaskCountUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "TaskExecution work area count query failed.");
            throw new WorkAreaTaskCountUnavailableException(
                "TaskExecution work area count query is unavailable.",
                exception);
        }
    }

    private void ForwardCorrelationId(HttpRequestMessage request)
    {
        var correlationId = httpContextAccessor.HttpContext?
            .Request.Headers[CorrelationIdHeader]
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdHeader, correlationId);
        }
    }

    private static void EnsureValidCounts(CountResponse data)
    {
        var hasNegativeCount = data.TotalTasks < 0 ||
            data.CancelledTasks < 0 ||
            data.EligibleTasks < 0 ||
            data.CompletedTasks < 0 ||
            data.OverdueTasks < 0;

        if (hasNegativeCount ||
            data.EligibleTasks != data.TotalTasks - data.CancelledTasks ||
            data.CompletedTasks > data.EligibleTasks ||
            data.OverdueTasks > data.EligibleTasks)
        {
            throw new WorkAreaTaskCountUnavailableException(
                "TaskExecution returned inconsistent work area counts.");
        }
    }

    private sealed record CountEnvelope(IReadOnlyList<CountResponse> Data);

    private sealed record CountResponse(
        Guid WorkAreaId,
        long TotalTasks,
        long CancelledTasks,
        long EligibleTasks,
        long CompletedTasks,
        long OverdueTasks,
        DateTimeOffset CalculatedAt);
}
