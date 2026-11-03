using System.Security.Cryptography;
using System.Text;

namespace Resource.Domain.Files;

// Also serves as the durable reservation journal if the process dies before the business transaction commits.
public sealed class AttachmentIntent
{
    public Guid Id { get; set; }
    public Guid FileId { get; set; }
    public Guid ReferenceId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string Purpose { get; set; } = "";
    public string State { get; set; } = "RESERVING";
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static Guid Identity(Guid fileId, Guid referenceId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"Resource:{referenceId:D}:{fileId:D}"))[..16]);

    // A committed business reference is authoritative even if a timeout left the journal in RESERVING.
    public string RecoveryAction(bool businessRecordContainsFile) => businessRecordContainsFile ? "FINALIZE" : "RELEASE";
}
