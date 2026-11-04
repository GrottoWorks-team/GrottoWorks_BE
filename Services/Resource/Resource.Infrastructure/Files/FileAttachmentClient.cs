using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Resource.Application.Files;
using Resource.Domain;

namespace Resource.Infrastructure.Files;

public sealed record NotificationAuditClientOptions(Uri BaseAddress, string InternalApiKey)
{
    public const string BaseAddressConfigurationKey = "NotificationAuditClient:BaseAddress";
}

/// <summary>Typed client for NotificationAudit <c>GET /internal/files/{id}</c>, forwarding the correlation id.</summary>
public sealed class FileAttachmentClient(
    HttpClient httpClient,
    NotificationAuditClientOptions options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<FileAttachmentClient> logger)

{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task EnsureAttachableAsync(
        IReadOnlyCollection<Guid> fileIds,
        string purpose,
        Guid uploaderUserId,
        CancellationToken cancellationToken = default)
    {
        // Checked in parallel (at most 10 evidence / 5 receipt files per request).
        await Task.WhenAll(fileIds.Distinct().Select(fileId => EnsureAsync(fileId, purpose, uploaderUserId, cancellationToken)));
    }

    private async Task EnsureAsync(Guid fileId, string purpose, Guid uploaderUserId, CancellationToken cancellationToken)
    {
        var file = await GetAsync(fileId, cancellationToken)
            ?? throw DomainRuleException.Validation("FILE_NOT_FOUND", $"File {fileId} does not exist.");
        if (file.Status is not ("UPLOADED" or "ATTACHED"))
        {
            throw DomainRuleException.Validation("FILE_NOT_UPLOADED", $"File {fileId} is not uploaded yet; complete the upload first.");
        }

        if (file.Purpose != purpose)
        {
            throw DomainRuleException.Validation("FILE_PURPOSE_MISMATCH", $"File {fileId} was uploaded for {file.Purpose}, not {purpose}.");
        }

        if (file.UploadedByUserId != uploaderUserId)
        {
            throw DomainRuleException.Validation("FILE_NOT_OWNED", $"File {fileId} was uploaded by another user.");
        }
    }

    private async Task<FileResponse?> GetAsync(Guid fileId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"internal/files/{fileId:D}");
            request.Headers.TryAddWithoutValidation("X-Internal-Api-Key", options.InternalApiKey);
            var correlationId = httpContextAccessor.HttpContext?.Request.Headers["X-Correlation-Id"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
            }

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("NotificationAudit file lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
                throw Unavailable();
            }

            var envelope = await response.Content.ReadFromJsonAsync<Envelope>(JsonOptions, cancellationToken);
            return envelope?.Data ?? throw Unavailable();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DomainRuleException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "NotificationAudit file lookup failed.");
            throw Unavailable();
        }
    }

    public async Task CommandAsync(Resource.Domain.Files.AttachmentIntent intent, string action, CancellationToken ct)
    {
        object payload = action.Length == 0
            ? new { reservationId = intent.Id, uploadedByUserId = intent.UploadedByUserId, service = "Resource", referenceType = "PURCHASE", referenceId = intent.ReferenceId,
                purpose = intent.Purpose == "PURCHASE_RECEIPT" ? "PURCHASE_RECEIPT" : "READINESS_EVIDENCE" }
            : new { reservationId = intent.Id, service = "Resource", referenceType = "PURCHASE", referenceId = intent.ReferenceId };
        using var response = await PostAsync($"internal/files/{intent.FileId:D}/attachment-reservations{action}", payload, ct);
    }

    public async Task<RecordFileDownload> DownloadAsync(Guid fileId, Guid referenceId, CancellationToken ct)
    {
        using var response = await PostAsync($"internal/files/{fileId:D}/record-download-url",
            new { service = "Resource", referenceType = "PURCHASE", referenceId }, ct);
        try { return (await response.Content.ReadFromJsonAsync<DownloadEnvelope>(JsonOptions, ct))?.Data ?? throw Unavailable(); }
        catch (JsonException) { throw Unavailable(); }
    }

    private async Task<HttpResponseMessage> PostAsync(string path, object payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.InternalApiKey) || options.InternalApiKey.Length < 32) throw Unavailable();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
            request.Headers.TryAddWithoutValidation("X-Internal-Api-Key", options.InternalApiKey);
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", httpContextAccessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N"));
            var response = await httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return response;
            var status = response.StatusCode;
            response.Dispose();
            if (status == HttpStatusCode.NotFound && path.EndsWith("/release", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (status is HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                throw DomainRuleException.Validation("FILE_ATTACHMENT_INVALID", "The file cannot be attached to this record.");
            if (status == HttpStatusCode.Conflict)
                throw new DomainRuleException("FILE_ATTACHMENT_CONFLICT", "The file is reserved for another business record.");
            throw Unavailable();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not DomainRuleException) { throw Unavailable(); }
    }

    private sealed record DownloadEnvelope(RecordFileDownload? Data);

    private static DomainRuleException Unavailable() =>
        new("FILE_SERVICE_UNAVAILABLE", "Files cannot be checked right now; try again shortly.", DomainErrorKind.ServiceUnavailable);

    private sealed record Envelope(FileResponse? Data);

    private sealed record FileResponse(Guid Id, Guid UploadedByUserId, string Purpose, string Status);
}
