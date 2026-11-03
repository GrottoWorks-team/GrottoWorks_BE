using System.Text.Json;
using TaskExecution.Domain.Tasks;

namespace TaskExecution.Application.Tasks;

public sealed record EvidenceInput(Guid FileId, string? Caption);
public sealed record AddProgressCommand(int PercentComplete, string? Note, IReadOnlyList<EvidenceInput>? Evidence, int? Version = null);
public sealed record TaskProgressDto(Guid Id, Guid TaskId, Guid SubmittedByUserId, int PercentComplete,
    string? Note, DateTimeOffset SubmittedAt, bool IsSubmission, Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt, IReadOnlyList<ProgressEvidence> Evidence);
public sealed record TaskReviewPackage(TaskDto Task, string? CompletionCriteria,
    IReadOnlyList<TaskProgressDto> Progress, IReadOnlyList<TaskCompletionReview> Reviews,
    decimal ApprovedHours, JsonElement MaterialSummary);
public sealed record AttachmentIntent(Guid Id, Guid TaskId, Guid ProgressId, Guid FileId, Guid UploaderId, DateTimeOffset CreatedAt);
public sealed record FileDownload(string DownloadUrl, DateTimeOffset ExpiresAt);
public interface ITaskReviewStore
{
    Task<IReadOnlyList<TaskProgressDto>> ListAsync(Guid taskId, CancellationToken ct);
    Task<(IReadOnlyList<TaskProgressDto> Items,long Total)> GetPageAsync(Guid taskId,int page,int size,bool ascending,CancellationToken ct);
    Task<IReadOnlyList<TaskCompletionReview>> ReviewsAsync(Guid taskId, CancellationToken ct);
    Task<decimal> ApprovedHoursAsync(Guid taskId, CancellationToken ct);
    Task PrepareAttachmentsAsync(IReadOnlyList<AttachmentIntent> intents, CancellationToken ct);
    Task EnsureAttachmentsActiveAsync(IReadOnlyList<AttachmentIntent> intents, CancellationToken ct);
    Task<AttachmentIntent?> FindAttachmentAsync(Guid progressId, Guid fileId, CancellationToken ct);
    void Add(TaskProgress progress, IReadOnlyList<ProgressEvidence> evidence);
    void Add(TaskCompletionReview review);
    Task<TaskProgress?> LatestSubmissionAsync(Guid taskId, CancellationToken ct);
}
public interface ITaskFileGateway
{
    Task ReserveAsync(AttachmentIntent intent, CancellationToken ct);
    Task FinalizeAsync(AttachmentIntent intent, CancellationToken ct);
    Task ReleaseAsync(AttachmentIntent intent, CancellationToken ct);
    Task<FileDownload> DownloadAsync(Guid fileId, Guid progressId, CancellationToken ct);
}
public sealed record PurchaseDeliverySnapshot(Guid PurchaseId, Guid TaskId, string Status, DateTimeOffset? ConfirmedAt, long Version,
    Guid? ConfirmedByUserId=null,Guid? BuyerUserId=null);
public sealed record PurchaseRequestSnapshot(Guid Id, Guid WorkAreaId, string Status, long Version,
    IReadOnlyList<PurchaseDeliverySnapshot>? Purchases = null);
public interface ITaskResourceGateway
{
    Task<PurchaseRequestSnapshot> PurchaseRequestAsync(Guid id, CancellationToken ct);
    Task<JsonElement> MaterialSummaryAsync(Guid taskId, CancellationToken ct);
}
