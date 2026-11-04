using FluentAssertions;
using NotificationAudit.Application.SystemJobs;
using NotificationAudit.Domain.SystemJobs;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class SystemJobExecutionTests
{
    [Fact]
    public async Task Source_failure_persists_safe_manifest_without_publishing_partial_artifact()
    {
        var jobs = new Jobs();
        var storage = new Storage();
        var builder = new Builder { Failure = new SystemArtifactBuildException("SOURCE_FAILED", "{\"complete\":false,\"sources\":[{\"status\":\"FAILED\"}]}") };
        await new ExecuteSystemJob(builder, storage, jobs).HandleAsync(Claimed(), default);
        storage.Uploads.Should().Be(0);
        jobs.Completed.Should().BeFalse();
        jobs.Error.Should().Be("SOURCE_FAILED");
        jobs.Manifest.Should().Contain("FAILED");
    }

    [Fact]
    public async Task Ambiguous_commit_does_not_delete_potentially_published_artifact()
    {
        var jobs = new Jobs { ThrowOnComplete = true };
        var storage = new Storage();
        await new ExecuteSystemJob(new Builder(), storage, jobs).HandleAsync(Claimed(), default);
        storage.Uploads.Should().Be(1);
        storage.Deletes.Should().Be(0);
        jobs.Error.Should().Be("SYSTEM_JOB_DEPENDENCY_FAILED");
        jobs.Manifest.Should().NotContain("password");
    }

    [Fact]
    public async Task Shutdown_leaves_lease_for_recovery_and_never_claims_success()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var jobs = new Jobs();
        var storage = new Storage();
        Func<Task> execute = () => new ExecuteSystemJob(new Builder { Failure = new OperationCanceledException(cancellation.Token) }, storage, jobs)
            .HandleAsync(Claimed(), cancellation.Token);
        await execute.Should().ThrowAsync<OperationCanceledException>();
        jobs.Completed.Should().BeFalse();
        jobs.Error.Should().BeNull();
    }

    private static SystemJob Claimed()
    {
        var job = SystemJob.Create(Guid.NewGuid(), SystemJobKind.Export, Guid.NewGuid(), "hash", "[]", DateTimeOffset.UtcNow, null);
        job.Claim(Guid.NewGuid(), DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        return job;
    }
    private sealed class Builder : ISystemArtifactBuilder
    {
        public Exception? Failure { get; init; }
        public Task<GeneratedSystemArtifact> BuildAsync(SystemJob job, CancellationToken ct)
        {
            if (Failure is not null) throw Failure;
            return Task.FromResult(new GeneratedSystemArtifact(new MemoryStream([1, 2, 3]), 3, "checksum", "application/zip", ".zip", "{}"));
        }
    }
    private sealed class Storage : ISystemArtifactStorage
    {
        public int Uploads { get; private set; }
        public int Deletes { get; private set; }
        public Task UploadAsync(string key, GeneratedSystemArtifact artifact, CancellationToken ct) { Uploads++; return Task.CompletedTask; }
        public Task DeleteAsync(string key, CancellationToken ct) { Deletes++; return Task.CompletedTask; }
    }
    private sealed class Jobs : ISystemJobExecutionStore
    {
        public bool ThrowOnComplete { get; init; }
        public bool Completed { get; private set; }
        public string? Error { get; private set; }
        public string? Manifest { get; private set; }
        public Task<SystemJob?> ClaimAsync(SystemJobKind kind, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> RenewAsync(Guid id, Guid token, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CompleteAsync(Guid id, Guid token, string key, GeneratedSystemArtifact artifact, CancellationToken ct)
        {
            if (ThrowOnComplete) throw new IOException("password=never-persist-this");
            Completed = true;
            return Task.FromResult(true);
        }
        public Task FailAsync(Guid id, Guid token, string code, string manifest, CancellationToken ct)
        { Error = code; Manifest = manifest; return Task.CompletedTask; }
    }
}
