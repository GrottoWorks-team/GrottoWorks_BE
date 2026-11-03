using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TaskExecution.Application.Events;
using TaskExecution.Application.Tasks;
using TaskExecution.Application.Users;
using TaskExecution.Infrastructure.Identity;
using Xunit;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class VolunteerDirectoryCacheTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Fresh_directory_page_is_cached_with_short_expiry_and_scope_in_the_key()
    {
        var fixture = new Fixture();
        var first = await fixture.Client.GetPageAsync(fixture.ParishId, null, 1, 100, default);
        var second = await fixture.Client.GetPageAsync(fixture.ParishId, null, 1, 100, default);
        second.Items.Single().UserId.Should().Be(first.Items.Single().UserId);
        fixture.Transport.Calls.Should().Be(1);
        fixture.Cache.LastKey.Should().Contain(fixture.ParishId.ToString("N")).And.EndWith(":all:1:100");
        fixture.Cache.Expiry.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Cache_failure_falls_back_and_pauses_further_cache_calls()
    {
        var fixture = new Fixture();
        fixture.Cache.FailGet = true;
        await fixture.Client.GetPageAsync(fixture.ParishId, null, 1, 100, default);
        await fixture.Client.GetPageAsync(fixture.ParishId, null, 1, 100, default);
        fixture.Cache.GetCalls.Should().Be(1);
        fixture.Transport.Calls.Should().Be(2);
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(31);
        fixture.Cache.FailGet = false;
        await fixture.Client.GetPageAsync(fixture.ParishId, null, 1, 100, default);
        await fixture.Client.GetPageAsync(fixture.ParishId, null, 1, 100, default);
        fixture.Transport.Calls.Should().Be(3);
    }

    [Fact]
    public async Task Cached_data_from_another_parish_is_discarded()
    {
        var fixture = new Fixture();
        var alien = fixture.User with { ParishId = Guid.NewGuid() };
        fixture.Cache.Value = JsonSerializer.SerializeToUtf8Bytes(new VolunteerDirectoryPage([alien], 1), JsonOptions);
        var result = await fixture.Client.GetPageAsync(fixture.ParishId, null, 1, 100, default);
        result.Items.Single().ParishId.Should().Be(fixture.ParishId);
        fixture.Transport.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed_as_a_cache_outage()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> query = () => fixture.Client.GetPageAsync(fixture.ParishId, null, 1, 100, cancellation.Token);
        await query.Should().ThrowAsync<OperationCanceledException>();
        fixture.Cache.GetCalls.Should().Be(0);
        fixture.Transport.Calls.Should().Be(0);
    }

    private sealed class Fixture
    {
        public Guid ParishId { get; } = Guid.NewGuid();
        public IdentityUser User { get; }
        public Cache Cache { get; } = new();
        public Clock Clock { get; } = new();
        public Transport Transport { get; }
        public CachedVolunteerDirectory Client { get; }
        public Fixture()
        {
            User = new(Guid.NewGuid(), "Volunteer", "ACTIVE", "VOLUNTEER", ParishId, null, [], [], []);
            Transport = new(JsonSerializer.Serialize(new { data = new[] { User }, page = new { page = 1, size = 100, totalItems = 1 } }, JsonOptions));
            var source = new IdentityVolunteerDirectory(new HttpClient(Transport) { BaseAddress = new("http://identity.api:8080/") },
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["InternalApi:ApiKey"] = new string('k', 32) }).Build(), new Correlation());
            Client = new(source, Cache, new(Clock), NullLogger<CachedVolunteerDirectory>.Instance);
        }
    }
    private sealed class Correlation : ITaskCorrelationContext { public string CorrelationId => "cache-test"; }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-11-03T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Transport(string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") }); }
    }
    private sealed class Cache : IDistributedCache
    {
        public byte[]? Value { get; set; }
        public bool FailGet { get; set; }
        public int GetCalls { get; private set; }
        public string? LastKey { get; private set; }
        public TimeSpan? Expiry { get; private set; }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            GetCalls++; LastKey = key;
            if (FailGet) throw new TimeoutException("Cache timeout");
            return Task.FromResult(Value);
        }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        { LastKey = key; Value = value; Expiry = options.AbsoluteExpirationRelativeToNow; return Task.CompletedTask; }
        public byte[]? Get(string key) => throw new NotSupportedException();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException();
        public void Refresh(string key) => throw new NotSupportedException();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
        public void Remove(string key) => Value = null;
        public Task RemoveAsync(string key, CancellationToken token = default) { Value = null; return Task.CompletedTask; }
    }
}
