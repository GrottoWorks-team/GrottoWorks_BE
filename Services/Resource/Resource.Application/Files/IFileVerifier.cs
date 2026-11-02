namespace Resource.Application.Files;

/// <summary>Purposes of NotificationAudit <c>file_object</c> (F-NTF-01) this service attaches.</summary>
public static class FilePurposes
{
    public const string ReadinessEvidence = "READINESS_EVIDENCE";
    public const string PurchaseReceipt = "PURCHASE_RECEIPT";
}

/// <summary>
/// API Contract §9: business requests reference only completed uploads; the file must exist, be UPLOADED (or already
/// ATTACHED), have the right purpose and belong to the caller. Checked against NotificationAudit
/// <c>GET /internal/files/{id}</c>; 422 when a file does not qualify, 503 when NotificationAudit cannot answer.
/// </summary>
public interface IFileVerifier
{
    Task EnsureAttachableAsync(IReadOnlyCollection<Guid> fileIds, string purpose, Guid uploaderUserId, CancellationToken cancellationToken = default);
}

/// <summary>Used when no NotificationAudit address is configured (tests, isolated runs): ids are kept as given.</summary>
public sealed class NoFileVerifier : IFileVerifier
{
    public Task EnsureAttachableAsync(IReadOnlyCollection<Guid> fileIds, string purpose, Guid uploaderUserId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
