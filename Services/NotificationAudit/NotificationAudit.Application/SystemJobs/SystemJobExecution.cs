using NotificationAudit.Domain.SystemJobs;

namespace NotificationAudit.Application.SystemJobs;

public sealed record GeneratedSystemArtifact(Stream Content, long Length, string Sha256,
    string ContentType, string Extension, string ManifestJson) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public interface ISystemArtifactBuilder
{
    Task<GeneratedSystemArtifact> BuildAsync(SystemJob job, CancellationToken cancellationToken);
}

public sealed class SystemArtifactBuildException(string code, string manifestJson, Exception? inner = null)
    : Exception(code, inner)
{
    public string Code { get; } = code;
    public string ManifestJson { get; } = manifestJson;
}

public interface ISystemArtifactStorage
{
    Task UploadAsync(string key, GeneratedSystemArtifact artifact, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public interface ISystemJobExecutionStore
{
    Task<SystemJob?> ClaimAsync(SystemJobKind kind, CancellationToken cancellationToken);
    Task<bool> RenewAsync(Guid id, Guid token, CancellationToken cancellationToken);
    Task<bool> CompleteAsync(Guid id, Guid token, string key, GeneratedSystemArtifact artifact,
        CancellationToken cancellationToken);
    Task FailAsync(Guid id, Guid token, string code, string manifest, CancellationToken cancellationToken);
}

/// <summary>Build then publish; a fenced metadata transaction alone makes the artifact visible.</summary>
public sealed class ExecuteSystemJob(ISystemArtifactBuilder builder, ISystemArtifactStorage storage,
    ISystemJobExecutionStore jobs)
{
    public async Task HandleAsync(SystemJob job, CancellationToken cancellationToken)
    {
        var token = job.LeaseToken ?? throw new InvalidOperationException("A job lease is required.");
        string? key = null;
        try
        {
            await using var artifact = await builder.BuildAsync(job, cancellationToken);
            key = $"system-jobs/{job.Id:N}/{token:N}{artifact.Extension}";
            await storage.UploadAsync(key, artifact, cancellationToken);
            await jobs.CompleteAsync(job.Id, token, key, artifact, cancellationToken);
        }
        catch (SystemArtifactBuildException exception)
        {
            await jobs.FailAsync(job.Id, token, exception.Code, exception.ManifestJson, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leave RUNNING for lease recovery; no success or failed dependency inferred from shutdown.
            throw;
        }
        catch (Exception)
        {
            // Never persist raw dependency errors: URLs, process stderr and connection strings may contain secrets.
            await jobs.FailAsync(job.Id, token, "SYSTEM_JOB_DEPENDENCY_FAILED",
                "{\"complete\":false,\"errorCode\":\"SYSTEM_JOB_DEPENDENCY_FAILED\"}", cancellationToken);
        }
        // Uploaded objects are reconciled by a delayed cleaner. A timed-out commit may have succeeded.
    }
}
