using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using NotificationAudit.Application.SystemJobs;
using NotificationAudit.Domain.SystemJobs;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class SystemJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Expired_lease_can_be_reclaimed_and_old_worker_cannot_complete()
    {
        var job = NewJob();
        var first = Guid.NewGuid();
        job.Claim(first, Now, TimeSpan.FromMinutes(5));
        var second = Guid.NewGuid();
        job.Claim(second, Now.AddMinutes(6), TimeSpan.FromMinutes(5));
        Action stale = () => job.Complete(first, Guid.NewGuid(), "old", "{}", Now.AddMinutes(6), Now.AddDays(7));
        stale.Should().Throw<InvalidOperationException>();
        job.Status.Should().Be(SystemJobStatus.Running);
        job.Complete(second, Guid.NewGuid(), "new", "{}", Now.AddMinutes(6), Now.AddDays(7));
        job.ArtifactObjectKey.Should().Be("new");
        job.Attempts.Should().Be(2);
    }

    [Fact]
    public void Live_lease_blocks_another_claim_and_expired_lease_cannot_publish()
    {
        var job = NewJob();
        var token = Guid.NewGuid();
        job.Claim(token, Now, TimeSpan.FromMinutes(5));
        Action duplicate = () => job.Claim(Guid.NewGuid(), Now.AddMinutes(1), TimeSpan.FromMinutes(5));
        duplicate.Should().Throw<InvalidOperationException>();
        Action expired = () => job.Complete(token, Guid.NewGuid(), "key", "{}", Now.AddMinutes(5), Now.AddDays(7));
        expired.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Failure_schedules_retry_without_a_success_artifact_then_exhausts()
    {
        var job = NewJob();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var now = job.NextAttemptAt;
            var token = Guid.NewGuid();
            job.Claim(token, now, TimeSpan.FromMinutes(5));
            job.Fail(token, "SOURCE_FAILED", "{\"complete\":false}", now, 3);
            job.ArtifactFileId.Should().BeNull();
            job.LeaseToken.Should().BeNull();
            job.Status.Should().Be(attempt == 3 ? SystemJobStatus.Failed : SystemJobStatus.Queued);
        }
        Action reclaim = () => job.Claim(Guid.NewGuid(), Now.AddDays(1), TimeSpan.FromMinutes(5));
        reclaim.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Default_seven_sources_and_case_order_normalization_have_same_fingerprint()
    {
        var store = new CaptureStore();
        var handler = new SystemJobHandler(new User(), store, TimeProvider.System);
        var key = Guid.NewGuid().ToString();
        var first = await handler.CreateAsync(SystemJobKind.Export, null, key, null, default);
        var fingerprint = store.Last!.Fingerprint;
        await handler.CreateAsync(SystemJobKind.Export, SystemServiceCatalog.All.Reverse().Select(s => s.ToLowerInvariant()).ToArray(), key, null, default);
        store.Last!.Fingerprint.Should().Be(fingerprint);
        first.Services.Should().HaveCount(7);
        store.Last.Kind.Should().Be(SystemJobKind.Export);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Invalid_idempotency_key_never_creates_job(string key)
    {
        var store = new CaptureStore();
        var handler = new SystemJobHandler(new User(), store, TimeProvider.System);
        Func<Task> action = () => handler.CreateAsync(SystemJobKind.Backup, null, key, null, default);
        (await action.Should().ThrowAsync<DomainException>()).Which.StatusCode.Should().Be(400);
        store.Last.Should().BeNull();
    }

    [Fact]
    public async Task Non_admin_is_rejected_before_persistence_and_backup_lookup_is_owner_scoped()
    {
        var store = new CaptureStore();
        var user = new User { Role = "LEADER" };
        var handler = new SystemJobHandler(user, store, TimeProvider.System);
        Func<Task> create = () => handler.CreateAsync(SystemJobKind.Export, null, Guid.NewGuid().ToString(), null, default);
        (await create.Should().ThrowAsync<DomainException>()).Which.StatusCode.Should().Be(403);
        store.Last.Should().BeNull();
        user.Role = "ADMIN";
        Func<Task> lookup = () => handler.GetBackupAsync(Guid.NewGuid(), default);
        (await lookup.Should().ThrowAsync<DomainException>()).Which.StatusCode.Should().Be(404);
        store.LookupOwner.Should().Be(user.UserId);
        store.LookupKind.Should().Be(SystemJobKind.Backup);
    }

    [Fact]
    public void Unknown_duplicate_and_empty_service_selections_are_rejected()
    {
        foreach (var selection in new[] { Array.Empty<string>(), new[] { "Identity", "identity" }, new[] { "Identity;DROP TABLE app_user" } })
        {
            Action validate = () => SystemServiceCatalog.Normalize(selection);
            validate.Should().Throw<DomainException>();
        }
    }

    private static SystemJob NewJob() => SystemJob.Create(Guid.NewGuid(), SystemJobKind.Export, Guid.NewGuid(), "fingerprint", "[]", Now, null);
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = Guid.NewGuid();
        public string Role { get; set; } = "ADMIN";
        public Guid? ParishId => null;
        public Guid? CommunityId => null;
    }
    private sealed class CaptureStore : ISystemJobStore
    {
        public SystemJob? Last { get; private set; }
        public Guid LookupOwner { get; private set; }
        public SystemJobKind LookupKind { get; private set; }
        public Task<SystemJob> CreateOrReplayAsync(SystemJob proposed, CancellationToken ct) { Last = proposed; return Task.FromResult(proposed); }
        public Task<SystemJob?> FindAsync(Guid id, Guid owner, SystemJobKind kind, CancellationToken ct)
        { LookupOwner = owner; LookupKind = kind; return Task.FromResult<SystemJob?>(null); }
    }
}
