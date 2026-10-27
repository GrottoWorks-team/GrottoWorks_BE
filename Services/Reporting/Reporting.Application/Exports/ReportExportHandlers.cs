using Reporting.Application.Reports;
using Reporting.Domain;
using Reporting.Domain.Exports;

namespace Reporting.Application.Exports;

/// <summary><c>ExportJob</c> (F-RPT-06): status and, once SUCCEEDED, the file reference.</summary>
public sealed record ExportJobDto(
    Guid Id,
    ReportType ReportType,
    ExportFormat Format,
    ExportStatus Status,
    ReportFiltersDto Filters,
    string? FileName,
    string? ContentType,
    long? FileSize,
    string? DownloadUrl,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt)
{
    public static ExportJobDto From(ReportExportJob job) =>
        new(job.ReportExportJobId, Enum.Parse<ReportType>(job.ReportType), job.Format, job.Status,
            new ReportFiltersDto(job.SeasonId, job.WorkAreaId, job.CommunityId, job.FromDate, job.ToDate),
            job.FileName, job.ContentType, job.FileSize,
            job.Status == ExportStatus.Succeeded ? $"/api/v1/report-exports/{job.ReportExportJobId}/file" : null,
            job.ErrorCode, job.ErrorMessage, job.CreatedAt, job.StartedAt, job.CompletedAt);
}

public sealed record CreateReportExportCommand(ReportType ReportType, ExportFormat Format, ReportQuery Query, string? IdempotencyKey);

public sealed record ExportFile(string FileName, string ContentType, byte[] Content);

public sealed record RenderedFile(byte[] Content, string ContentType, string Extension);

public interface IReportFileRenderer
{
    RenderedFile Render(ReportDataDto report, ExportFormat format);
}

/// <summary>Wakes the export worker as soon as a job is queued (it also polls).</summary>
public interface IExportSignal
{
    void Notify();
}

public interface IReportExportStore
{
    /// <summary>Tracked.</summary>
    Task<ReportExportJob?> GetAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<ReportExportJob?> FindByIdempotencyKeyAsync(Guid userId, string key, CancellationToken cancellationToken = default);

    /// <summary>Marks the oldest QUEUED job RUNNING and returns it; null when none is left (or another worker won it).</summary>
    Task<ReportExportJob?> ClaimNextAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Re-queues (or fails) jobs RUNNING since before <paramref name="startedBefore"/>.</summary>
    Task<int> RecoverStaleAsync(DateTimeOffset startedBefore, DateTimeOffset now, CancellationToken cancellationToken = default);

    void Add(ReportExportJob job);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// F-RPT-06 <c>createReportExport</c> / <c>getReportExport</c> (PARISH): the request is validated now (season of the parish,
/// filters) and built later by the worker. An <c>Idempotency-Key</c> returns the job already created for it.
/// </summary>
public sealed class ReportExportHandler(ReportScopeResolver resolver, IReportExportStore store, IExportSignal signal)
{
    public async Task<(ExportJobDto Job, bool Created)> CreateAsync(CreateReportExportCommand command, CancellationToken cancellationToken = default)
    {
        var requester = resolver.Caller();
        ReportScopeResolver.EnsureRole(command.ReportType, requester);
        var query = command.Query;
        var type = command.ReportType.ToString();
        if (command.IdempotencyKey is { } key && !string.IsNullOrWhiteSpace(key) &&
            await store.FindByIdempotencyKeyAsync(requester.UserId, key.Trim(), cancellationToken) is { } existing)
        {
            return existing.SameRequest(type, command.Format, query.SeasonId, query.WorkAreaId, query.CommunityId, query.From, query.To)
                ? (ExportJobDto.From(existing), false)
                : throw new DomainRuleException("IDEMPOTENCY_KEY_REUSED", "This Idempotency-Key was used for a different export.");
        }

        await resolver.ResolveAsync(query, requester, cancellationToken);
        var job = ReportExportJob.Queue(requester.ParishId, requester.UserId, requester.Role, type, command.Format, query.SeasonId, query.WorkAreaId,
            query.CommunityId, query.From, query.To, command.IdempotencyKey, DateTimeOffset.UtcNow);
        store.Add(job);
        await store.SaveChangesAsync(cancellationToken);
        signal.Notify();
        return (ExportJobDto.From(job), true);
    }

    public async Task<ExportJobDto> GetAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        ExportJobDto.From(await LoadAsync(jobId, cancellationToken));

    public async Task<ExportFile> DownloadAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await LoadAsync(jobId, cancellationToken);
        if (job.Status != ExportStatus.Succeeded || job.Content is null)
        {
            throw new DomainRuleException("EXPORT_NOT_READY", $"The export is {job.Status.ToString().ToUpperInvariant()}; download it once it SUCCEEDED.");
        }

        return new ExportFile(job.FileName!, job.ContentType!, job.Content);
    }

    /// <summary>Jobs of another parish are 404 (BR-67).</summary>
    private async Task<ReportExportJob> LoadAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await store.GetAsync(jobId, cancellationToken);
        if (job is null || job.ParishId != resolver.Caller().ParishId)
        {
            throw DomainRuleException.NotFound("EXPORT_NOT_FOUND", "The export job does not exist.");
        }

        return job;
    }
}

/// <summary>Runs one queued export as its requester (same BR-57 scope as on screen) and stores the rendered file.</summary>
public sealed class ReportExportRunner(IReportExportStore store, ReportQueryHandler reports, IReportFileRenderer renderer)
{
    /// <returns>false when no job was waiting.</returns>
    public async Task<bool> RunNextAsync(CancellationToken cancellationToken = default)
    {
        var job = await store.ClaimNextAsync(DateTimeOffset.UtcNow, cancellationToken);
        if (job is null)
        {
            return false;
        }

        try
        {
            var report = await reports.BuildAsync(
                Enum.Parse<ReportType>(job.ReportType),
                new ReportQuery(job.SeasonId, job.WorkAreaId, job.CommunityId, job.FromDate, job.ToDate),
                new Reports.ReportRequester(job.RequestedByUserId, job.RequestedByRole, job.ParishId),
                cancellationToken);
            var file = renderer.Render(report, job.Format);
            var name = $"grottoworks-{job.ReportType.ToLowerInvariant()}-{report.GeneratedAt.ToOffset(TimeSpan.FromHours(7)):yyyyMMdd-HHmm}.{file.Extension}";
            job.Succeed(name, file.ContentType, file.Content, DateTimeOffset.UtcNow);
        }
        catch (DomainRuleException exception)
        {
            job.Fail(exception.Code, exception.Message, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            job.Fail("EXPORT_FAILED", exception.Message, DateTimeOffset.UtcNow);
        }

        await store.SaveChangesAsync(cancellationToken);
        return true;
    }
}
