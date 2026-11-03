using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NotificationAudit.Application.Files;
using NotificationAudit.Application.SystemJobs;
using NotificationAudit.Domain.Files;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class SystemArtifactAccessTests
{
    [Theory]
    [InlineData("VOLUNTEER", false, 300)]
    [InlineData("ADMIN", true, 300)]
    [InlineData("ADMIN", false, -1)]
    [InlineData("ADMIN", false, 30)]
    public async Task Non_admin_foreign_owner_expired_and_nearly_expired_artifacts_have_no_download_url(string role, bool foreign, int seconds)
    {
        var user = new User(role);
        var file = FileObject.CreateSystemArtifact(foreign ? Guid.NewGuid() : user.UserId, Guid.NewGuid(),
            "system-jobs/job/attempt.zip", "system.zip", "application/zip", 12, "checksum", false, DateTimeOffset.UtcNow);
        var storage = new FileFinalizationTests.Storage([]);
        var handler = new GetFileDownloadHandler(user, new FileFinalizationTests.Repository(file, []), storage,
            Options.Create(new FileUploadOptions()), new Access(DateTimeOffset.UtcNow.AddSeconds(seconds)));
        Func<Task> download = () => handler.HandleAsync(file.Id, default);
        (await download.Should().ThrowAsync<DomainException>()).Which.StatusCode.Should().Be(404);
        storage.Downloaded.Should().BeEmpty();
    }

    [Fact]
    public async Task Admin_owner_gets_the_artifact_url_and_attached_artifact_cannot_be_deleted()
    {
        var user = new User("ADMIN");
        var file = FileObject.CreateSystemArtifact(user.UserId, Guid.NewGuid(), "system-jobs/job/attempt.zip.enc",
            "backup.zip.enc", "application/octet-stream", 12, "checksum", true, DateTimeOffset.UtcNow);
        var repository = new FileFinalizationTests.Repository(file, []);
        var storage = new FileFinalizationTests.Storage([]);
        var handler = new GetFileDownloadHandler(user, repository, storage, Options.Create(new FileUploadOptions()), new Access(DateTimeOffset.UtcNow.AddHours(1)));
        await handler.HandleAsync(file.Id, default);
        storage.Downloaded.Should().Equal(file.ObjectKey);
        Func<Task> delete = () => new DeleteUnattachedFileHandler(user, repository, storage).HandleAsync(file.Id, default);
        (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("FILE_ATTACHED");
    }

    private sealed class Access(DateTimeOffset expiresAt) : ISystemArtifactAccess
    {
        public Task<DateTimeOffset?> GetExpiryAsync(Guid fileId, Guid owner, CancellationToken ct) => Task.FromResult<DateTimeOffset?>(expiresAt);
    }
    private sealed class User(string role) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = Guid.NewGuid();
        public string Role => role;
        public Guid? ParishId => null;
        public Guid? CommunityId => null;
    }
}
