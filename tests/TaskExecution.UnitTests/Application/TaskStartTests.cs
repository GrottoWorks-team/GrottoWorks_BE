using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskExecution.Api.Endpoints;
using TaskExecution.Application;
using TaskExecution.Application.Events;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Tasks;
using TaskExecution.Application.Users;
using TaskExecution.Contracts.Events;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Scopes;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.UnitTests.Application;

public sealed class TaskStartTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Task_start_http_routes_require_leader_and_return_warnings_and_etag()
    {
        var fixture = new Fixture();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddApplication();
        builder.Services.AddSingleton(fixture.Service);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapTaskEndpoints();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var path = $"/api/v1/tasks/{fixture.Task.Id}";
        client.DefaultRequestHeaders.Add("X-Test-Role", "VOLUNTEER");
        using var forbidden = await client.GetAsync(path + "/start-eligibility");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var forbiddenStart = await client.PostAsync(path + "/start", null);
        forbiddenStart.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Add("X-Test-Role", "LEADER");
        using var eligibility = await client.GetAsync(path + "/start-eligibility");
        eligibility.StatusCode.Should().Be(HttpStatusCode.OK);
        using var preflight = JsonDocument.Parse(await eligibility.Content.ReadAsStringAsync());
        preflight.RootElement.GetProperty("data").GetProperty("canStart").GetBoolean().Should().BeTrue();
        using var request = new HttpRequestMessage(HttpMethod.Post, path + "/start");
        var initialETag = $"\"{fixture.Task.Version}\"";
        request.Headers.TryAddWithoutValidation("If-Match", initialETag);
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.Should().Be($"\"{fixture.Task.Version}\"");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("task").GetProperty("startedByUserId").GetGuid().Should().Be(fixture.Source.User.UserId);
        data.GetProperty("warnings")[0].GetProperty("code").GetString().Should().Be("TASK_START_NOT_DUE");

        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, path + "/start");
        replayRequest.Headers.TryAddWithoutValidation("If-Match", initialETag);
        using var replay = await client.SendAsync(replayRequest);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.Headers.ETag!.Tag.Should().Be(response.Headers.ETag!.Tag);
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        replayJson.RootElement.GetProperty("data").GetProperty("warnings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Assigned_leader_can_start_early_and_records_the_actor_and_event()
    {
        var fixture = new Fixture();
        var eligibility = await fixture.Service.GetEligibilityAsync(fixture.Task.Id, default);
        eligibility.CanStart.Should().BeTrue();
        eligibility.Warnings.Should().ContainSingle().Which.Code.Should().Be("TASK_START_NOT_DUE");
        eligibility.MaterialsKnown.Should().BeTrue();

        var result = await fixture.Service.StartAsync(fixture.Task.Id, fixture.Task.Version, default);
        result.Task.Status.Should().Be(TaskState.InProgress);
        result.Task.StartedAt.Should().Be(Now);
        result.Task.StartedByUserId.Should().Be(fixture.Source.User.UserId);
        result.Warnings.Should().Equal(eligibility.Warnings);
        fixture.Resource.Calls.Should().Be(0);
        var message = fixture.Source.Events.Items.Should().ContainSingle().Which;
        message.EventType.Should().Be(TaskEventTypes.StatusChanged);
        message.Payload.GetProperty("status").GetString().Should().Be("IN_PROGRESS");
        message.Payload.GetProperty("startedAt").GetDateTimeOffset().Should().Be(Now);

        var replay = await fixture.Service.StartAsync(fixture.Task.Id, result.Task.Version - 1, default);
        replay.Task.Should().BeEquivalentTo(result.Task);
        replay.Warnings.Should().BeEmpty();
        (await fixture.Service.GetEligibilityAsync(fixture.Task.Id, default)).CanStart.Should().BeFalse();
        fixture.Source.Events.Items.Should().ContainSingle();
    }

    [Theory]
    [InlineData("LEADER")]
    [InlineData("VOLUNTEER")]
    [InlineData("MO")]
    public async Task Other_users_cannot_check_or_start_the_task(string role)
    {
        var fixture = new Fixture(needsMaterials: true);
        fixture.Source.User.UserId = Guid.NewGuid();
        fixture.Source.User.Role = role;
        var get = () => fixture.Service.GetEligibilityAsync(fixture.Task.Id, default);
        var start = () => fixture.Service.StartAsync(fixture.Task.Id, null, default);
        await get.Should().ThrowAsync<DomainRuleException>().Where(error => error.Kind == DomainErrorKind.Forbidden);
        await start.Should().ThrowAsync<DomainRuleException>().Where(error => error.Kind == DomainErrorKind.Forbidden);
        fixture.Resource.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Planning_season_blocks_start_but_zero_accepted_members_only_warns()
    {
        var fixture = new Fixture(accepted: false, seasonStatus: SeasonScopeStatus.Planning);
        var eligibility = await fixture.Service.GetEligibilityAsync(fixture.Task.Id, default);
        eligibility.CanStart.Should().BeFalse();
        eligibility.AcceptedMembers.Should().Be(0);
        eligibility.Blockers.Select(reason => reason.Code).Should()
            .Equal("TASK_START_SEASON_NOT_ACTIVE");
        eligibility.Warnings.Should().Contain(reason => reason.Code == "TASK_NOT_ENOUGH_MEMBERS");
        var start = () => fixture.Service.StartAsync(fixture.Task.Id, null, default);
        await start.Should().ThrowAsync<DomainRuleException>().Where(error => error.Code == "TASK_START_SEASON_NOT_ACTIVE");
        fixture.Source.Events.Items.Should().BeEmpty();
        fixture.Source.Season.Apply(fixture.Source.Area.ParishId, fixture.Source.Season.StartDate,
            fixture.Source.Season.EndDate, SeasonScopeStatus.Active, 3, Now);
        var result = await start();
        result.Task.Status.Should().Be(TaskState.InProgress);
        result.Warnings.Should().Contain(reason => reason.Code == "TASK_NOT_ENOUGH_MEMBERS");
    }

    [Theory]
    [InlineData("shortage", "TASK_NOT_ENOUGH_MATERIALS", true)]
    [InlineData("outage", "TASK_START_MATERIALS_UNAVAILABLE", false)]
    [InlineData("stale", "TASK_START_MATERIALS_UNAVAILABLE", false)]
    [InlineData("malformed", "TASK_START_MATERIALS_UNAVAILABLE", false)]
    public async Task Materials_that_are_short_or_unverified_warn_without_blocking_start(string scenario, string code, bool known)
    {
        var fixture = new Fixture(needsMaterials: true);
        fixture.Resource.Scenario = scenario;
        var eligibility = await fixture.Service.GetEligibilityAsync(fixture.Task.Id, default);
        eligibility.CanStart.Should().BeTrue();
        eligibility.MaterialsKnown.Should().Be(known);
        eligibility.Blockers.Should().BeEmpty();
        eligibility.Warnings.Should().Contain(reason => reason.Code == code);
        if (known)
            eligibility.MaterialShortages.Should().ContainSingle().Which.AllocatedQuantity.Should().Be(2);
        var result = await fixture.Service.StartAsync(fixture.Task.Id, null, default);
        result.Task.Status.Should().Be(TaskState.InProgress);
        result.Warnings.Should().Equal(eligibility.Warnings);
        var calls = fixture.Resource.Calls;
        (await fixture.Service.StartAsync(fixture.Task.Id, result.Task.Version - 1, default)).Warnings.Should().BeEmpty();
        fixture.Resource.Calls.Should().Be(calls);
        fixture.Source.Events.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Purchase_is_excluded_from_explicit_start()
    {
        var fixture = new Fixture(purchase: true);
        var eligibility = await fixture.Service.GetEligibilityAsync(fixture.Task.Id, default);
        eligibility.CanStart.Should().BeFalse();
        eligibility.Blockers.Should().ContainSingle().Which.Code.Should().Be("TASK_TYPE_INVALID");
        var start = () => fixture.Service.StartAsync(fixture.Task.Id, null, default);
        await start.Should().ThrowAsync<DomainRuleException>().Where(error => error.Code == "TASK_TYPE_INVALID");
        fixture.Task.BeginWork(Now);
        await start.Should().ThrowAsync<DomainRuleException>().Where(error => error.Code == "TASK_TYPE_INVALID");
        fixture.Resource.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_starts_share_the_task_lock_and_record_one_event()
    {
        var fixture = new Fixture(needsMaterials: true);
        fixture.Resource.WaitForBoth = true;
        var version = fixture.Task.Version;
        var results = await Task.WhenAll(
            fixture.Service.StartAsync(fixture.Task.Id, version, default),
            fixture.Service.StartAsync(fixture.Task.Id, version, default));
        results.Should().OnlyContain(result => result.Task.Version == version + 1);
        results.Should().ContainSingle(result => result.Warnings.Count == 0);
        fixture.Task.Version.Should().Be(version + 1);
        fixture.Source.Events.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Open_task_must_be_marked_ready_and_a_stale_version_cannot_start_a_ready_task()
    {
        var fixture = new Fixture(ready: false);
        var version = fixture.Task.Version;
        var start = () => fixture.Service.StartAsync(fixture.Task.Id, version, default);
        await start.Should().ThrowAsync<DomainRuleException>().Where(error => error.Code == "TASK_NOT_PUBLISHED");
        fixture.Task.MarkReady(null, Now);
        await start.Should().ThrowAsync<DomainRuleException>().Where(error => error.Code == "TASK_VERSION_MISMATCH");
        fixture.Source.Events.Items.Should().BeEmpty();
    }

    private sealed class HeaderAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggers, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggers, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(user, Scheme.Name)));
        }
    }

    private sealed class Fixture
    {
        public AssignmentTestFixture Source { get; } = new();
        public WorkTask Task { get; }
        public MaterialResource Resource { get; }
        public TaskStartService Service { get; }

        public Fixture(bool needsMaterials = false, bool accepted = true, bool purchase = false,
            SeasonScopeStatus seasonStatus = SeasonScopeStatus.Active, bool ready = true)
        {
            var material = Guid.NewGuid();
            Task = WorkTask.CreateOpen(Guid.NewGuid(), Source.Area.WorkAreaId, Source.User.UserId, "Task", null, null,
                TaskPriority.Medium, 60, 1, Now.AddHours(1), Now.AddDays(1), Now);
            if (purchase) Task.SetSpecialType(TaskType.Purchase, Guid.NewGuid());
            if (needsMaterials) Task.ReplaceMaterials([new(material, 4, null)], null, Now);
            if (ready) Task.MarkReady(null, Now);
            Source.Season.Apply(Source.Area.ParishId, Source.Season.StartDate, Source.Season.EndDate, seasonStatus, 2, Now);
            var assignment = TaskAssignment.Assign(Task.Id, Source.VolunteerId, Source.User.UserId, null, false, false, null, Now);
            if (accepted) assignment.Accept(null, Now);
            Source.Store.Items.Add(assignment);
            var scopes = new AssignmentTestFixture.TestScopes(Source.Season, Source.Area);
            Resource = new MaterialResource(Task.WorkAreaId, material);
            Service = new TaskStartService(new AssignmentTestFixture.TestTasks(Task), Source.Store,
                new TaskScopeAccess(scopes, Source.User), scopes, new ActiveUserAccess(Source.Identity, Source.User),
                Resource, new TaskEventRecorder(Source.Events, Source.User, new AssignmentTestFixture.TestCorrelation()),
                Source.User, new FixedClock());
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MaterialResource(Guid areaId, Guid materialId) : ITaskResourceGateway
    {
        private readonly TaskCompletionSource bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;
        public int Calls => calls;
        public bool WaitForBoth { get; set; }
        public string? Scenario { get; set; }
        public async Task<JsonElement> MaterialSummaryAsync(Guid taskId, CancellationToken ct)
        {
            if (Interlocked.Increment(ref calls) == 2) bothArrived.TrySetResult();
            if (WaitForBoth) await bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            if (Scenario == "outage") throw new DomainRuleException("RESOURCE_SERVICE_UNAVAILABLE", "Unavailable", DomainErrorKind.ServiceUnavailable);
            if (Scenario == "malformed") return JsonSerializer.SerializeToElement(new { taskId, workAreaId = areaId, requirementVersion = 1, materials = new[] { "invalid" } });
            return JsonSerializer.SerializeToElement(new
            {
                taskId, workAreaId = areaId, requirementVersion = 1,
                materials = new[] { new { materialId, requiredQuantity = Scenario == "stale" ? 3 : 4, allocatedQuantity = Scenario == "shortage" ? 2 : 4 } }
            });
        }
        public Task<PurchaseRequestSnapshot> PurchaseRequestAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }
}
