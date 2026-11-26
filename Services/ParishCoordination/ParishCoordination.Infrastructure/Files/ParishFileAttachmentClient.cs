using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using BuildingBlocks.Web.Exceptions;
using Polly;
using ParishCoordination.Application.Parishes;

namespace ParishCoordination.Infrastructure.Files;

public sealed record NotificationAuditClientOptions(Uri BaseAddress, string InternalApiKey)
{
    public const string BaseAddressConfigurationKey = "NotificationAuditClient:BaseAddress";
}

public sealed class ParishFileAttachmentClient(
    HttpClient httpClient,
    NotificationAuditClientOptions options,
    IHttpContextAccessor httpContextAccessor)
    : IParishFileAttachmentClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ParishMapFileDto?> GetFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"internal/files/{fileId:D}");
        request.Headers.TryAddWithoutValidation("X-Internal-Api-Key", options.InternalApiKey);
        AddCorrelationId(request);
        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSucceededAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<FileEnvelope>(JsonOptions, cancellationToken);
        var file = envelope?.Data;
        return file is null ? null : new ParishMapFileDto(file.Id, file.UploadedByUserId, file.Purpose, file.Status,
            file.FileName, file.ReferenceType, file.ReferenceId);
    }

    public Task ReserveAsync(Guid fileId, Guid parishId, Guid uploaderUserId, Guid reservationId, CancellationToken cancellationToken = default) =>
        PostAsync($"internal/files/{fileId:D}/attachment-reservations",
            new { reservationId, uploadedByUserId = uploaderUserId, service = "ParishCoordination", referenceType = "PARISH", referenceId = parishId, purpose = "MAP_IMAGE" },
            cancellationToken);

    public Task FinalizeAsync(Guid fileId, Guid parishId, Guid reservationId, CancellationToken cancellationToken = default) =>
        PostAsync($"internal/files/{fileId:D}/attachment-reservations/finalize",
            new { reservationId, service = "ParishCoordination", referenceType = "PARISH", referenceId = parishId },
            cancellationToken);

    public Task ReleaseAsync(Guid fileId, Guid parishId, Guid reservationId, CancellationToken cancellationToken = default) =>
        PostAsync($"internal/files/{fileId:D}/attachment-reservations/release",
            new { reservationId, service = "ParishCoordination", referenceType = "PARISH", referenceId = parishId },
            cancellationToken, ignoreMissing: true);

    private async Task PostAsync(string path, object payload, CancellationToken cancellationToken, bool ignoreMissing = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        request.Headers.TryAddWithoutValidation("X-Internal-Api-Key", options.InternalApiKey);
        AddCorrelationId(request);
        using var response = await SendAsync(request, cancellationToken);
        if (ignoreMissing && response.StatusCode == System.Net.HttpStatusCode.NotFound) return;
        await EnsureSucceededAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await httpClient.SendAsync(request, ct);
        }
        catch (Exception error) when (!ct.IsCancellationRequested &&
            error is HttpRequestException or OperationCanceledException or ExecutionRejectedException)
        {
            throw new DomainException("FILE_SERVICE_UNAVAILABLE", "NotificationAudit is temporarily unavailable.", 503);
        }
    }

    private static async Task EnsureSucceededAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var status = (int)response.StatusCode;
        if (status is not (403 or 404 or 409 or 422))
            throw new DomainException("FILE_SERVICE_UNAVAILABLE", "NotificationAudit is temporarily unavailable.", 503);

        var problem = await response.Content.ReadFromJsonAsync<FileProblem>(JsonOptions, ct);
        throw new DomainException(problem?.Code ?? "FILE_ATTACHMENT_CONFLICT",
            problem?.Detail ?? "NotificationAudit rejected the attachment operation.", status);
    }

    private void AddCorrelationId(HttpRequestMessage request)
    {
        var correlationId = httpContextAccessor.HttpContext?.TraceIdentifier;
        if (!string.IsNullOrWhiteSpace(correlationId))
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
    }

    private sealed record FileEnvelope(FileResponse? Data);
    private sealed record FileResponse(Guid Id, Guid UploadedByUserId, string Purpose, string Status, string FileName,
        string? ReferenceType, Guid? ReferenceId);
    private sealed record FileProblem(string? Code, string? Detail);
}
