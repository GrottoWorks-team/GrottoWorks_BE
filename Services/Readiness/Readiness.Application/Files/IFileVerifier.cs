namespace Readiness.Application.Files;

public static class FilePurposes
{
    public const string ReadinessEvidence = "READINESS_EVIDENCE";
    public const string PurchaseReceipt = "PURCHASE_RECEIPT";
}

public interface IFileVerifier
{
    Task EnsureAttachableAsync(IReadOnlyCollection<Guid> fileIds, string purpose, Guid uploaderUserId, CancellationToken cancellationToken = default);

    // The lease excludes reconciliation until the business transaction has finished. Disposal never releases a file.
    // Prepared finalize intents are saved by the business unit of work, together with its file references.
    Task<IAsyncDisposable> ReserveAsync(IReadOnlyCollection<Guid> fileIds, string purpose, Guid uploaderUserId,
        Guid referenceId, CancellationToken cancellationToken = default);
}

public sealed record RecordFileDownload(string DownloadUrl, DateTimeOffset ExpiresAt);

public interface IRecordFileDownload
{
    Task<RecordFileDownload> GetAsync(Guid fileId, Guid referenceId, CancellationToken cancellationToken);
}
