using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ParishCoordination.Infrastructure.TaskScopeSync;

namespace ParishCoordination.UnitTests;

public sealed class TaskScopeDeliveryTests
{
    [Fact]
    public async Task Work_area_delivery_sends_parent_first_with_authentication_and_exact_versions()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var options = Options();
        var client = new HttpClient(handler) { BaseAddress = options.BaseAddress };
        var areaId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var payload = new WorkAreaDelivery(
            new SeasonSnapshot(Guid.NewGuid(), new DateOnly(2026, 10, 1), new DateOnly(2027, 1, 15), "PLANNING", 4),
            new WorkAreaSnapshot(seasonId, Guid.NewGuid(), "Area", null, Guid.NewGuid(), null,
                null, null, "PLANNED", 7));
        var message = new TaskScopeOutbox
        {
            ScopeType = "work-area", AggregateId = areaId, SourceVersion = 7,
            Payload = JsonSerializer.Serialize(payload, ScopeJson.Options)
        };

        await new HttpTaskScopeDelivery(client, options).DeliverAsync(message, default);

        handler.Paths.Should().Equal($"/internal/season-scopes/{seasonId}", $"/internal/work-area-scopes/{areaId}");
        handler.Keys.Should().OnlyContain(key => key == options.ApiKey);
        using var body = JsonDocument.Parse(handler.Bodies[1]);
        body.RootElement.GetProperty("version").GetInt64().Should().Be(7);
        body.RootElement.GetProperty("leaderUserId").GetGuid().Should().Be(payload.WorkArea.LeaderUserId!.Value);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(503)]
    public async Task Failed_parent_is_not_followed_by_work_area_request(int status)
    {
        var handler = new RecordingHandler((HttpStatusCode)status);
        var options = Options();
        var message = new TaskScopeOutbox
        {
            ScopeType = "work-area", AggregateId = Guid.NewGuid(),
            Payload = JsonSerializer.Serialize(new WorkAreaDelivery(
                new SeasonSnapshot(Guid.NewGuid(), new DateOnly(2026, 10, 1), new DateOnly(2027, 1, 15), "PLANNING", 1),
                new WorkAreaSnapshot(Guid.NewGuid(), Guid.NewGuid(), "Area", null, null, null,
                    null, null, "PLANNED", 1)), ScopeJson.Options)
        };
        var delivery = new HttpTaskScopeDelivery(new HttpClient(handler) { BaseAddress = options.BaseAddress }, options);

        var act = () => delivery.DeliverAsync(message, default);

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be((HttpStatusCode)status);
        handler.Paths.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 60)]
    [InlineData(5, 300)]
    [InlineData(100, 300)]
    public void Retry_is_capped(int attempts, int seconds) =>
        TaskScopeSyncOptions.RetryDelay(attempts).Should().Be(TimeSpan.FromSeconds(seconds));

    [Fact]
    public void Enabled_sync_requires_internal_key_and_accepts_docker_address()
    {
        var values = new Dictionary<string, string?>
        {
            ["TaskScopeSync:Enabled"] = "true",
            ["TaskScopeSync:BaseAddress"] = "http://taskexecution.api:8080/"
        };
        var invalid = () => TaskScopeSyncOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(values).Build());
        invalid.Should().Throw<InvalidOperationException>();
        values["InternalApi:ApiKey"] = new string('x', 32);
        var options = TaskScopeSyncOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(values).Build());
        options.RequestTimeout.Should().Be(TimeSpan.FromSeconds(10));
        options.PollInterval.Should().Be(TimeSpan.FromSeconds(2));
        options.BaseAddress.Host.Should().Be("taskexecution.api");
    }

    private static TaskScopeSyncOptions Options() => new(true, new Uri("http://taskexecution.api:8080/"),
        new string('x', 32), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<string> Keys { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Keys.Add(request.Headers.GetValues("X-Internal-Api-Key").Single());
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status);
        }
    }
}
