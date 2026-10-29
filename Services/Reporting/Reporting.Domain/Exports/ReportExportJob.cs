namespace Reporting.Domain.Exports;

public enum ExportFormat
{
    Xlsx,
    Pdf,
    Csv
}

public enum ExportStatus
{
    Queued,
    Running,
    Succeeded,
    Failed
}

/// <summary>
/// <c>report_export_job</c> (spec §3 technical table, F-RPT-06): an asynchronous export of one report (202 + job). The
/// report is built as the requester (same BR-57 scope as the screen); until F-NTF-01 stores files in MinIO the generated
/// file is kept on the job. A job stuck in RUNNING (worker crash) is queued again, at most <see cref="MaxAttempts"/> times.
/// </summary>
public sealed class ReportExportJob
{
    public const int MaxAttempts = 3;
    public const int MaxIdempotencyKeyLength = 100;

    private ReportExportJob()
    {
    }

    public Guid ReportExportJobId { get; private set; }

    public Guid ParishId { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public string RequestedByRole { get; private set; } = string.Empty;

    /// <summary>Report type name (TASKS, MATERIALS, ...).</summary>
    public string ReportType { get; private set; } = string.Empty;

    public ExportFormat Format { get; private set; }

    public Guid SeasonId { get; private set; }

    public Guid? WorkAreaId { get; private set; }

    public Guid? CommunityId { get; private set; }

    public DateOnly? FromDate { get; private set; }

    public DateOnly? ToDate { get; private set; }

    public ExportStatus Status { get; private set; }

    public string? IdempotencyKey { get; private set; }

    public int Attempts { get; private set; }

    public string? FileName { get; private set; }

    public string? ContentType { get; private set; }

    public byte[]? Content { get; private set; }

    public long? FileSize { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Optimistic concurrency token (<c>xmin</c>): two workers never run the same job.</summary>
    public uint Version { get; private set; }

    public static ReportExportJob Queue(
        Guid parishId,
        Guid userId,
        string role,
        string reportType,
        ExportFormat format,
        Guid seasonId,
        Guid? workAreaId,
        Guid? communityId,
        DateOnly? from,
        DateOnly? to,
        string? idempotencyKey,
        DateTimeOffset now)
    {
        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (key is { Length: > MaxIdempotencyKeyLength })
        {
            throw new DomainRuleException("IDEMPOTENCY_KEY_INVALID", $"Idempotency-Key must not exceed {MaxIdempotencyKeyLength} characters.", DomainErrorKind.BadRequest);
        }

        return new ReportExportJob
        {
            ReportExportJobId = Guid.NewGuid(),
            ParishId = parishId,
            RequestedByUserId = userId,
            RequestedByRole = role,
            ReportType = reportType,
            Format = format,
            SeasonId = seasonId,
            WorkAreaId = workAreaId,
            CommunityId = communityId,
            FromDate = from,
            ToDate = to,
            Status = ExportStatus.Queued,
            IdempotencyKey = key,
            CreatedAt = now.ToUniversalTime()
        };
    }

    /// <summary>Same idempotency key must mean the same request.</summary>
    public bool SameRequest(string reportType, ExportFormat format, Guid seasonId, Guid? workAreaId, Guid? communityId, DateOnly? from, DateOnly? to) =>
        ReportType == reportType && Format == format && SeasonId == seasonId && WorkAreaId == workAreaId && CommunityId == communityId &&
        FromDate == from && ToDate == to;

    public void Start(DateTimeOffset now)
    {
        if (Status != ExportStatus.Queued)
        {
            throw new InvalidOperationException($"Export job {ReportExportJobId} is {Status}, not QUEUED.");
        }

        Status = ExportStatus.Running;
        Attempts++;
        StartedAt = now.ToUniversalTime();
    }

    public void Succeed(string fileName, string contentType, byte[] content, DateTimeOffset now)
    {
        Status = ExportStatus.Succeeded;
        FileName = fileName;
        ContentType = contentType;
        Content = content;
        FileSize = content.LongLength;
        ErrorCode = null;
        ErrorMessage = null;
        CompletedAt = now.ToUniversalTime();
    }

    public void Fail(string code, string message, DateTimeOffset now)
    {
        Status = ExportStatus.Failed;
        ErrorCode = code;
        ErrorMessage = message.Length > 500 ? message[..500] : message;
        CompletedAt = now.ToUniversalTime();
    }

    /// <summary>A RUNNING job whose worker disappeared: queue it again, or fail it after the last attempt.</summary>
    public void Recover(DateTimeOffset now)
    {
        if (Status != ExportStatus.Running)
        {
            return;
        }

        if (Attempts >= MaxAttempts)
        {
            Fail("EXPORT_WORKER_LOST", "The export was interrupted too many times.", now);
            return;
        }

        Status = ExportStatus.Queued;
        StartedAt = null;
    }
}
