using BuildingBlocks.Security;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using NotificationAudit.Application.Files;
using NotificationAudit.Contracts.Files;
using NotificationAudit.Domain.Files;
using NotificationAudit.Infrastructure.Files;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class FileFinalizationTests
{
    [Fact]
    public async Task Completion_checks_the_private_copy_and_cleans_pending_only_after_commit()
    {
        var fixture = new Fixture();
        var pendingKey = fixture.File.ObjectKey;
        var result = await fixture.Complete.HandleAsync(fixture.File.Id, new(null), default);

        result.Id.Should().Be(fixture.File.Id);
        fixture.Storage.InspectedKey.Should().Be(fixture.File.ObjectKey).And.NotBe(pendingKey);
        fixture.Log.Should().Equal("copy", "inspect", "save", "commit", "delete");
        fixture.Storage.Deleted.Should().Equal(pendingKey);
        fixture.File.Status.Should().Be(FileObjectStatus.Uploaded);
        fixture.Log.Clear();
        await fixture.Complete.HandleAsync(fixture.File.Id, new(null), default);
        fixture.Log.Should().Equal("commit");
    }

    [Fact]
    public async Task Database_failure_preserves_pending_object_for_a_retry()
    {
        var fixture = new Fixture();
        fixture.Repository.FailSave = true;
        Func<Task> complete = () => fixture.Complete.HandleAsync(fixture.File.Id, new(null), default);
        await complete.Should().ThrowAsync<InvalidOperationException>();
        fixture.Storage.Deleted.Should().BeEmpty();
        fixture.Log.Should().Equal("copy", "inspect", "save");
    }

    [Fact]
    public async Task Invalid_private_copy_never_marks_file_completed()
    {
        var fixture = new Fixture();
        fixture.Storage.Inspection = new(99, "image/jpeg", "checksum", "image/jpeg");
        Func<Task> complete = () => fixture.Complete.HandleAsync(fixture.File.Id, new(null), default);
        (await complete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("FILE_SIZE_MISMATCH");
        fixture.File.Status.Should().Be(FileObjectStatus.PendingUpload);
        fixture.Log.Should().Equal("copy", "inspect");
    }

    [Fact]
    public async Task Delete_database_failure_does_not_remove_the_live_object()
    {
        var fixture = new Fixture();
        fixture.Repository.FailSave = true;
        var handler = new DeleteUnattachedFileHandler(fixture.User, fixture.Repository, fixture.Storage);
        Func<Task> delete = () => handler.HandleAsync(fixture.File.Id, default);
        await delete.Should().ThrowAsync<InvalidOperationException>();
        fixture.Storage.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_storage_delete_can_be_retried_using_the_durable_tombstone()
    {
        var fixture = new Fixture();
        fixture.Storage.FailDelete = true;
        var handler = new DeleteUnattachedFileHandler(fixture.User, fixture.Repository, fixture.Storage);
        Func<Task> delete = () => handler.HandleAsync(fixture.File.Id, default);
        (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("FILE_STORAGE_UNAVAILABLE");
        fixture.File.Status.Should().Be(FileObjectStatus.Deleted);
        fixture.Log.Should().Equal("save", "commit", "delete");
        fixture.Storage.FailDelete = false;
        fixture.Log.Clear();
        await handler.HandleAsync(fixture.File.Id, default);
        fixture.Log.Should().Equal("commit", "delete");
    }

    [Fact]
    public void Inspection_reads_no_more_than_one_byte_beyond_its_limit()
    {
        using var stream = new MemoryStream(new byte[1024]);
        Action read = () => S3FileObjectStorage.ReadBounded(stream, 16, default);
        read.Should().Throw<UploadedObjectTooLargeException>();
        stream.Position.Should().Be(17);
    }

    [Fact]
    public void Exact_limit_is_accepted_and_cancellation_is_honored()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        S3FileObjectStorage.ReadBounded(stream, 3, default).Should().Equal(1, 2, 3);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Action read = () => S3FileObjectStorage.ReadBounded(stream, 3, cancelled.Token);
        read.Should().Throw<OperationCanceledException>();
    }

    internal sealed class Fixture
    {
        public List<string> Log { get; } = [];
        public User User { get; } = new();
        public FileObject File { get; }
        public Repository Repository { get; }
        public Storage Storage { get; }
        public CompleteUploadHandler Complete { get; }
        public Fixture()
        {
            File = FileObject.CreateUploadRequest(User.UserId, "photo.jpg", "image/jpeg", 4, null,
                FilePurpose.ProgressEvidence, DateTimeOffset.UtcNow);
            Repository = new(File, Log);
            Storage = new(Log);
            Complete = new(User, Repository, Storage, TimeProvider.System);
        }
    }

    internal sealed class User : ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public bool IsAuthenticated => true;
        public string Role => "VOLUNTEER";
        public Guid? ParishId => null;
        public Guid? CommunityId => null;
    }

    internal sealed class Repository(FileObject file, List<string> log) : IFileObjectRepository, IFileObjectTransaction
    {
        public bool FailSave { get; set; }
        public void Add(FileObject value) => throw new NotSupportedException();
        public Task<IFileObjectTransaction> BeginTransactionAsync(CancellationToken cancellationToken) => Task.FromResult<IFileObjectTransaction>(this);
        public Task<FileObject?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<FileObject?>(id == file.Id ? file : null);
        public Task<FileObject?> GetAsync(Guid id, CancellationToken cancellationToken) => GetForUpdateAsync(id, cancellationToken);
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            log.Add("save");
            if (FailSave) throw new InvalidOperationException("Simulated database failure.");
            return Task.CompletedTask;
        }
        public Task CommitAsync(CancellationToken cancellationToken) { log.Add("commit"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class Storage(List<string> log) : IFileObjectStorage
    {
        public string? InspectedKey { get; private set; }
        public bool FailDelete { get; set; }
        public List<string> Deleted { get; } = [];
        public List<string> Downloaded { get; } = [];
        public StoredObjectInspection Inspection { get; set; } = new(4, "image/jpeg", "checksum", "image/jpeg");
        public Task CopyToPrivateAsync(string pending, string completed, CancellationToken cancellationToken)
        { log.Add("copy"); return Task.CompletedTask; }
        public Task<StoredObjectInspection> InspectAsync(string key, CancellationToken cancellationToken)
        { log.Add("inspect"); InspectedKey = key; return Task.FromResult(Inspection); }
        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            log.Add("delete");
            if (FailDelete) throw new FileStorageUnavailableException("Unavailable");
            Deleted.Add(key);
            return Task.CompletedTask;
        }
        public Task<PresignedObjectRequest> CreateUploadRequestAsync(string key, string type, TimeSpan lifetime, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PresignedObjectRequest> CreateDownloadRequestAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken)
        {
            Downloaded.Add(key);
            return Task.FromResult(new PresignedObjectRequest("https://storage.example/file", new Dictionary<string, string>(), DateTimeOffset.UtcNow.Add(lifetime)));
        }
    }
}
