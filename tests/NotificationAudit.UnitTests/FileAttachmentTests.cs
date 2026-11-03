using FluentAssertions;
using NotificationAudit.Domain.Files;
using NotificationAudit.Application.Files;
using NotificationAudit.Contracts.Files;
using BuildingBlocks.Web.Exceptions;
using Microsoft.Extensions.Options;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class FileAttachmentTests
{
    [Fact]
    public async Task Handler_checks_uploader_and_purpose_before_reserving()
    {
        var fixture = new FileFinalizationTests.Fixture();
        fixture.File.Complete("completed/private", new string('a', 64), DateTimeOffset.UtcNow);
        var handler = new FileAttachmentHandler(fixture.Repository, fixture.Storage, Options.Create(new FileUploadOptions()));
        var request = new ReserveFileAttachmentRequest(Guid.NewGuid(), Guid.NewGuid(), "TaskExecution", "TASK_PROGRESS",
            Guid.NewGuid(), FilePurposeDto.ProgressEvidence);
        Func<Task> wrongUploader = () => handler.ReserveAsync(fixture.File.Id, request, default);
        (await wrongUploader.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("FILE_ACCESS_DENIED");
        Func<Task> wrongPurpose = () => handler.ReserveAsync(fixture.File.Id,
            request with { UploadedByUserId = fixture.User.UserId, Purpose = FilePurposeDto.PurchaseReceipt }, default);
        (await wrongPurpose.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("FILE_PURPOSE_MISMATCH");
        fixture.File.AttachmentReservationId.Should().BeNull();
    }

    [Fact]
    public async Task Reserved_file_is_not_deletable_and_record_download_requires_finalization_and_exact_reference()
    {
        var fixture = new FileFinalizationTests.Fixture();
        fixture.File.Complete("completed/private", new string('a', 64), DateTimeOffset.UtcNow);
        var handler = new FileAttachmentHandler(fixture.Repository, fixture.Storage, Options.Create(new FileUploadOptions()));
        var request = new ReserveFileAttachmentRequest(Guid.NewGuid(), fixture.User.UserId, "TaskExecution", "TASK_PROGRESS",
            Guid.NewGuid(), FilePurposeDto.ProgressEvidence);
        await handler.ReserveAsync(fixture.File.Id, request, default);
        var deletion = new DeleteUnattachedFileHandler(fixture.User, fixture.Repository, fixture.Storage);
        Func<Task> delete = () => deletion.HandleAsync(fixture.File.Id, default);
        (await delete.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("FILE_RESERVED");
        var download = new FileRecordDownloadRequest(request.Service, request.ReferenceType, request.ReferenceId);
        Func<Task> prematureDownload = () => handler.DownloadAsync(fixture.File.Id, download, default);
        (await prematureDownload.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("FILE_NOT_FOUND");
        fixture.Storage.Downloaded.Should().BeEmpty();
        await handler.FinalizeAsync(fixture.File.Id,
            new(request.ReservationId, request.Service, request.ReferenceType, request.ReferenceId), default);
        Func<Task> wrongRecord = () => handler.DownloadAsync(fixture.File.Id, download with { ReferenceId = Guid.NewGuid() }, default);
        (await wrongRecord.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("FILE_NOT_FOUND");
        await handler.DownloadAsync(fixture.File.Id, download, default);
        fixture.Storage.Downloaded.Should().Equal("completed/private");
        fixture.Storage.Deleted.Should().BeEmpty();
    }

    [Fact]
    public void Reservation_blocks_delete_until_source_finalizes_or_releases()
    {
        var file = Uploaded();
        var operation = Guid.NewGuid();
        var record = Guid.NewGuid();
        file.ReserveAttachment(operation, "TaskExecution", "TASK_PROGRESS", record).Should().BeTrue();
        Action delete = file.MarkDeleted;
        delete.Should().Throw<InvalidOperationException>();
        file.ReserveAttachment(operation, "TaskExecution", "TASK_PROGRESS", record).Should().BeFalse();
        file.ReleaseAttachment(operation, "TaskExecution", "TASK_PROGRESS", record).Should().BeTrue();
        file.ReleaseAttachment(operation, "TaskExecution", "TASK_PROGRESS", record).Should().BeFalse();
        file.MarkDeleted();
        file.Status.Should().Be(FileObjectStatus.Deleted);
    }

    [Fact]
    public void Finalization_is_idempotent_and_preserves_ownership()
    {
        var file = Uploaded();
        var uploader = file.UploadedByUserId;
        var key = file.ObjectKey;
        var operation = Guid.NewGuid();
        var record = Guid.NewGuid();
        file.ReserveAttachment(operation, "TaskExecution", "TASK_PROGRESS", record);
        file.FinalizeAttachment(operation, "TaskExecution", "TASK_PROGRESS", record).Should().BeTrue();
        file.FinalizeAttachment(operation, "TaskExecution", "TASK_PROGRESS", record).Should().BeFalse();
        file.ReserveAttachment(operation, "TaskExecution", "TASK_PROGRESS", record).Should().BeFalse();
        file.Status.Should().Be(FileObjectStatus.Attached);
        file.ReferenceId.Should().Be(record);
        file.ReferenceType.Should().Be("TASK_PROGRESS");
        file.UploadedByUserId.Should().Be(uploader);
        file.ObjectKey.Should().Be(key);
        Action release = () => file.ReleaseAttachment(operation, "TaskExecution", "TASK_PROGRESS", record);
        release.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Another_service_record_or_operation_cannot_take_a_reservation()
    {
        var file = Uploaded();
        var operation = Guid.NewGuid();
        var record = Guid.NewGuid();
        file.ReserveAttachment(operation, "TaskExecution", "TASK_PROGRESS", record);
        Action otherService = () => file.FinalizeAttachment(operation, "Resource", "TASK_PROGRESS", record);
        Action otherRecord = () => file.FinalizeAttachment(operation, "TaskExecution", "TASK_PROGRESS", Guid.NewGuid());
        Action otherOperation = () => file.ReserveAttachment(Guid.NewGuid(), "TaskExecution", "TASK_PROGRESS", record);
        otherService.Should().Throw<InvalidOperationException>();
        otherRecord.Should().Throw<InvalidOperationException>();
        otherOperation.Should().Throw<InvalidOperationException>();
        file.Status.Should().Be(FileObjectStatus.Uploaded);
        file.AttachmentReservationId.Should().Be(operation);
    }

    [Fact]
    public void Pending_and_deleted_files_cannot_be_reserved()
    {
        var pending = FileObject.CreateUploadRequest(Guid.NewGuid(), "photo.jpg", "image/jpeg", 4, null,
            FilePurpose.ProgressEvidence, DateTimeOffset.UtcNow);
        Action reservePending = () => pending.ReserveAttachment(Guid.NewGuid(), "TaskExecution", "TASK_PROGRESS", Guid.NewGuid());
        reservePending.Should().Throw<InvalidOperationException>();
        var deleted = Uploaded();
        deleted.MarkDeleted();
        Action reserveDeleted = () => deleted.ReserveAttachment(Guid.NewGuid(), "TaskExecution", "TASK_PROGRESS", Guid.NewGuid());
        reserveDeleted.Should().Throw<InvalidOperationException>();
    }

    private static FileObject Uploaded()
    {
        var file = FileObject.CreateUploadRequest(Guid.NewGuid(), "photo.jpg", "image/jpeg", 4, null,
            FilePurpose.ProgressEvidence, DateTimeOffset.UtcNow);
        file.Complete("completed/private", new string('a', 64), DateTimeOffset.UtcNow);
        return file;
    }
}
