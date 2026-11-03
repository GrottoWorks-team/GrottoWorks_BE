using System.Security.Cryptography;
using System.Text;

namespace Readiness.Domain.Checklists;

public sealed class CorrectiveTaskIntent
{
    public Guid Id { get; set; }
    public Guid CheckId { get; set; }
    public Guid ItemId { get; set; }
    public Guid WorkAreaId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public string Payload { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public Guid? TaskId { get; set; }
    public string State { get; set; } = "PENDING";
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static string Hash(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    public void EnsureSameRequest(string fingerprint)
    {
        if (Fingerprint != fingerprint)
            throw new DomainRuleException("IDEMPOTENCY_KEY_REUSED", "This request ID was already used with different corrective task data.");
    }
}
