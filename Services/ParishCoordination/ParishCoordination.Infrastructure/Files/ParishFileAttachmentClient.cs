using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
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
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<FileEnvelope>(JsonOptions, cancellationToken);
        var file = envelope?.Data;
        return file is null ? null : new ParishMapFileDto(file.Id, file.UploadedByUserId, file.Purpose, file.Status, file.FileName);
    }

    public async Task AttachAsync(Guid fileId, Guid parishId, Guid uploaderUserId, Guid reservationId, CancellationToken cancellationToken = default)
    {
        await PostAsync($"internal/files/{fileId:D}/attachment-reservations",
            new { reservationId, uploadedByUserId = uploaderUserId, service = "ParishCoordination", referenceType = "PARISH", referenceId = parishId, purpose = "MAP_IMAGE" },
            cancellationToken);
        await PostAsync($"internal/files/{fileId:D}/attachment-reservations/finalize",
            new { reservationId, service = "ParishCoordination", referenceType = "PARISH", referenceId = parishId },
            cancellationToken);
    }

    public async Task ReleaseAsync(Guid fileId, Guid parishId, Guid reservationId, CancellationToken cancellationToken = default)
    {
        await PostAsync($"internal/files/{fileId:D}/attachment-reservations/release",
            new { reservationId, service = "ParishCoordination", referenceType = "PARISH", referenceId = parishId },
            cancellationToken);
    }

    private async Task PostAsync(string path, object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        request.Headers.TryAddWithoutValidation("X-Internal-Api-Key", options.InternalApiKey);
        AddCorrelationId(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"NotificationAudit attachment call failed: HTTP {(int)response.StatusCode}");
        }
    }

    private void AddCorrelationId(HttpRequestMessage request)
    {
        var correlationId = httpContextAccessor.HttpContext?.TraceIdentifier;
        if (!string.IsNullOrWhiteSpace(correlationId))
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
    }

    private sealed record FileEnvelope(FileResponse? Data);
    private sealed record FileResponse(Guid Id, Guid UploadedByUserId, string Purpose, string Status, string FileName);
}
