using System.Text;
using BuildingBlocks.Security;
using ClosedXML.Excel;
using Reporting.Application.Exports;
using Reporting.Application.Reports;
using Reporting.Application.Scope;
using Reporting.Domain;
using Reporting.Domain.Exports;
using Reporting.Domain.ReadModels;
using Reporting.Infrastructure.Exports;

namespace Reporting.UnitTests;

public sealed class ExportTests
{
    private readonly Guid _parishId = Guid.NewGuid();
    private readonly Guid _seasonId = Guid.NewGuid();
    private readonly FakeScopeStore _scopes = new();
    private readonly FakeReadModelStore _readModels = new();
    private readonly FakeReportExportStore _jobs = new();
    private readonly FakeExportSignal _signal = new();
    private readonly FakeCurrentUser _parish;

    public ExportTests()
    {
        _parish = new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Parish, _parishId);
        var area = _scopes.Seed(_parishId, _seasonId, name: "Hang đá");
        var line = MaterialLine.Create(area.WorkAreaId, Guid.NewGuid(), _seasonId, _parishId);
        line.SetBalance(100, 40, 60, "CAB-01", "=Dây \"điện\", loại 1", "m", DateTimeOffset.UtcNow);
        _readModels.Lines.Add(line);
    }

    private ReportScopeResolver Resolver(FakeCurrentUser user) => new(_scopes, new WorkAreaAccess(_scopes, user), user);

    private ReportExportHandler Handler(FakeCurrentUser? user = null) => new(Resolver(user ?? _parish), _jobs, _signal);

    private ReportExportRunner Runner() => new(_jobs, new ReportQueryHandler(Resolver(_parish), _readModels), new ReportFileRenderer());

    private CreateReportExportCommand Command(ExportFormat format = ExportFormat.Xlsx, string? key = null, Guid? season = null) =>
        new(ReportType.Materials, format, new ReportQuery(season ?? _seasonId), key);

    [Fact]
    public async Task Export_is_queued_validated_and_signalled()
    {
        var (job, created) = await Handler().CreateAsync(Command());

        created.Should().BeTrue();
        job.Status.Should().Be(ExportStatus.Queued);
        job.DownloadUrl.Should().BeNull();
        _signal.Notified.Should().Be(1);

        var unknownSeason = () => Handler().CreateAsync(Command(season: Guid.NewGuid()));
        (await unknownSeason.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("SEASON_NOT_FOUND");
    }

    [Fact]
    public async Task Idempotency_key_returns_the_same_job_or_rejects_a_different_request()
    {
        var (first, _) = await Handler().CreateAsync(Command(key: "k-1"));
        var (again, created) = await Handler().CreateAsync(Command(key: "k-1"));

        created.Should().BeFalse();
        again.Id.Should().Be(first.Id);
        _jobs.Jobs.Should().ContainSingle();

        var different = () => Handler().CreateAsync(Command(ExportFormat.Pdf, "k-1"));
        (await different.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("IDEMPOTENCY_KEY_REUSED");
    }

    [Theory]
    [InlineData(ExportFormat.Xlsx)]
    [InlineData(ExportFormat.Pdf)]
    [InlineData(ExportFormat.Csv)]
    public async Task Worker_renders_the_report_and_it_can_be_downloaded(ExportFormat format)
    {
        var (job, _) = await Handler().CreateAsync(Command(format));
        var notReady = () => Handler().DownloadAsync(job.Id);
        (await notReady.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("EXPORT_NOT_READY");

        (await Runner().RunNextAsync()).Should().BeTrue();
        (await Runner().RunNextAsync()).Should().BeFalse();

        var done = await Handler().GetAsync(job.Id);
        done.Status.Should().Be(ExportStatus.Succeeded);
        done.DownloadUrl.Should().Be($"/api/v1/report-exports/{job.Id}/file");
        var file = await Handler().DownloadAsync(job.Id);
        file.FileName.Should().StartWith("grottoworks-materials-").And.EndWith("." + format.ToString().ToLowerInvariant());
        file.Content.Length.Should().BeGreaterThan(100);

        switch (format)
        {
            case ExportFormat.Xlsx:
                using (var workbook = new XLWorkbook(new MemoryStream(file.Content)))
                {
                    var sheet = workbook.Worksheet(1);
                    sheet.Cell(1, 2).GetString().Should().Be("Báo cáo vật tư");
                    sheet.CellsUsed().Should().Contain(cell => cell.GetString() == "=Dây \"điện\", loại 1");
                    sheet.CellsUsed().Should().Contain(cell => cell.DataType == XLDataType.Number && cell.GetValue<decimal>() == 60);
                }

                break;
            case ExportFormat.Pdf:
                Encoding.ASCII.GetString(file.Content, 0, 5).Should().Be("%PDF-");
                break;
            default:
                var text = Encoding.UTF8.GetString(file.Content);
                text.Should().Contain("Báo cáo vật tư").And.Contain("\"'=Dây \"\"điện\"\", loại 1\"");
                break;
        }
    }

    [Fact]
    public async Task Failure_is_recorded_on_the_job()
    {
        var (job, _) = await Handler().CreateAsync(Command());
        _scopes.Scopes.Clear();

        await Runner().RunNextAsync();

        var failed = await Handler().GetAsync(job.Id);
        failed.Status.Should().Be(ExportStatus.Failed);
        failed.ErrorCode.Should().Be("SEASON_NOT_FOUND");
    }

    [Fact]
    public async Task Jobs_of_another_parish_are_not_found()
    {
        var (job, _) = await Handler().CreateAsync(Command());
        var outsider = new FakeCurrentUser(Guid.NewGuid(), GrottoWorksRoles.Parish, Guid.NewGuid());

        var get = () => Handler(outsider).GetAsync(job.Id);

        (await get.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("EXPORT_NOT_FOUND");
    }

    [Fact]
    public void Stale_running_job_is_requeued_then_failed_after_max_attempts()
    {
        var job = ReportExportJob.Queue(_parishId, Guid.NewGuid(), GrottoWorksRoles.Parish, "Materials", ExportFormat.Csv, _seasonId, null, null, null, null,
            null, DateTimeOffset.UtcNow);
        for (var attempt = 1; attempt < ReportExportJob.MaxAttempts; attempt++)
        {
            job.Start(DateTimeOffset.UtcNow);
            job.Recover(DateTimeOffset.UtcNow);
            job.Status.Should().Be(ExportStatus.Queued);
        }

        job.Start(DateTimeOffset.UtcNow);
        job.Recover(DateTimeOffset.UtcNow);

        job.Status.Should().Be(ExportStatus.Failed);
        job.ErrorCode.Should().Be("EXPORT_WORKER_LOST");
    }
}
