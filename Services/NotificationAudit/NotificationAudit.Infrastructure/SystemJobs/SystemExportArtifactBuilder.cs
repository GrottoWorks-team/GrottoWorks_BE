using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NotificationAudit.Application.SystemJobs;
using NotificationAudit.Domain.SystemJobs;
using NotificationAudit.Infrastructure.SystemExport;

namespace NotificationAudit.Infrastructure.SystemJobs;

public sealed class SystemExportArtifactBuilder(IHttpClientFactory clients, IOptions<SystemJobOptions> configuredOptions,
    SystemExportSource localSource, TimeProvider clock) : ISystemArtifactBuilder
{
    public async Task<GeneratedSystemArtifact> BuildAsync(SystemJob job, CancellationToken ct)
    {
        var options = configuredOptions.Value;
        var entries = new List<object>();
        var services = JsonSerializer.Deserialize<string[]>(job.ServicesJson)!;
        var file = ArtifactStreams.CreateTemporaryFile();
        var failed = false;
        string? manifestJson = null;
        try
        {
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var service in services)
                {
                    var startedAt = clock.GetUtcNow();
                    try
                    {
                        await using var source = ArtifactStreams.CreateTemporaryFile();
                        if (service == "NotificationAudit")
                        {
                            await localSource.WriteAsync(source, ct);
                            if (source.Length > options.MaximumSourceBytes) throw new IOException("Source exceeds limit.");
                        }
                        else
                        {
                            if (!options.Sources.TryGetValue(service, out var configuration) || configuration.ApiKey.Length < 32 ||
                                !Uri.TryCreate(configuration.BaseUrl, UriKind.Absolute, out var baseUri) ||
                                (baseUri.Scheme != "https" && baseUri.Scheme != "http") || !string.IsNullOrEmpty(baseUri.UserInfo) ||
                                !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
                                throw new InvalidOperationException("Export source is not configured.");
                            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            timeout.CancelAfter(TimeSpan.FromMinutes(options.SourceTimeoutMinutes));
                            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/internal/system-export"));
                            request.Headers.Add("X-System-Export-Key", configuration.ApiKey);
                            if (job.CorrelationId.HasValue) request.Headers.Add("X-Correlation-Id", job.CorrelationId.Value.ToString());
                            using var response = await clients.CreateClient("SystemExport").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                            response.EnsureSuccessStatusCode();
                            await using var responseStream = await response.Content.ReadAsStreamAsync(timeout.Token);
                            await ArtifactStreams.CopyHashedAsync(responseStream, source, options.MaximumSourceBytes, timeout.Token);
                        }
                        var footer = await ArtifactStreams.ValidateJsonLinesAsync(source, service, ct);
                        var hash = await ArtifactStreams.HashAsync(source, ct);
                        var entry = archive.CreateEntry($"{service}.jsonl", CompressionLevel.Fastest);
                        await using (var destination = entry.Open()) await source.CopyToAsync(destination, ct);
                        entries.Add(new { service, status = "COMPLETED", startedAt, finishedAt = clock.GetUtcNow(), bytes = source.Length, sha256 = hash, source = footer });
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception)
                    {
                        failed = true;
                        entries.Add(new { service, status = "FAILED", startedAt, finishedAt = clock.GetUtcNow(), errorCode = "SOURCE_EXPORT_FAILED" });
                    }
                }
                manifestJson = JsonSerializer.Serialize(Manifest(job, entries, !failed));
                await using var manifestStream = archive.CreateEntry("manifest.json").Open();
                await manifestStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(manifestJson), ct);
            }
            if (failed) throw new SystemArtifactBuildException("SYSTEM_EXPORT_SOURCE_FAILED", manifestJson!);
            var checksum = await ArtifactStreams.HashAsync(file, ct);
            return new GeneratedSystemArtifact(file, file.Length, checksum, "application/zip", ".zip", manifestJson!);
        }
        catch { await file.DisposeAsync(); throw; }
    }

    private object Manifest(SystemJob job, List<object> sources, bool complete) => new
    {
        schemaVersion = 1, jobId = job.Id, kind = "SYSTEM_EXPORT", complete, createdAt = job.CreatedAt,
        finishedAt = clock.GetUtcNow(), consistency = "Independent repeatable-read snapshots per service; not a simultaneous system snapshot.",
        excludes = new[] { "authentication credentials", "tokens", "event payloads", "private download URLs", "unstructured audit state" }, sources
    };
}
