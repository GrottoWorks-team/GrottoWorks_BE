using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Readiness.Application.Checklists;
using Readiness.Domain;

namespace Readiness.Infrastructure.Checklists;

public sealed class TaskCorrectiveClient(HttpClient http, IConfiguration configuration)
{
    public async Task<CreatedCorrectiveTask> CreateAsync(CorrectiveTaskCreationRequest request, CancellationToken ct)
    {
        var key = configuration["InternalApi:ApiKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32) throw Unavailable();
        var task = request.Task;
        using var message = new HttpRequestMessage(HttpMethod.Post, "internal/tasks")
        {
            Content = JsonContent.Create(new { request.ReadinessItemId, request.WorkAreaId, request.RequestedByUserId,
                task.Name, task.Description, task.CompletionCriteria, task.Priority, task.EstimatedMinutes,
                requiredVolunteers = task.RequiredVolunteerCount, task.StartAt, task.Deadline })
        };
        message.Headers.Add("X-Internal-Api-Key", key);
        message.Headers.Add("Idempotency-Key", task.RequestId.ToString("D"));
        message.Headers.Add("X-Correlation-Id", task.RequestId.ToString("D"));
        try
        {
            using var response = await http.SendAsync(message, ct);
            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new DomainRuleException("CORRECTIVE_TASK_CONFLICT", "Task service rejected the corrective task request; retry using the original request data.");
            if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest)
                throw DomainRuleException.Validation("CORRECTIVE_TASK_INVALID", "Task service rejected the corrective task schedule or definition.");
            if (!response.IsSuccessStatusCode) throw Unavailable();
            var data = (await response.Content.ReadFromJsonAsync<Envelope>(cancellationToken: ct))?.Data;
            if (data is null || data.Id == Guid.Empty || data.WorkAreaId != request.WorkAreaId || data.TaskType != "CORRECTIVE" ||
                data.Status is not ("OPEN" or "NOT_STARTED" or "IN_PROGRESS" or "SUBMITTED" or "REVISION_REQUIRED" or "COMPLETED"))
                throw Unavailable();
            return new CreatedCorrectiveTask(data.Id,data.WorkAreaId,data.TaskType,data.Status);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException or
            Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException)
        { throw Unavailable(); }
    }
    private static DomainRuleException Unavailable() => new("TASK_SERVICE_UNAVAILABLE", "Corrective creation is pending reconciliation; retry with the same request ID.", DomainErrorKind.ServiceUnavailable);
    private sealed record TaskReply(Guid Id,Guid WorkAreaId,string TaskType,string Status);
    private sealed record Envelope(TaskReply? Data);
}
