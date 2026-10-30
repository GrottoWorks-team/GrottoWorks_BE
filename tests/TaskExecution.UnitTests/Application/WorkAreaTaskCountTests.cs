using FluentAssertions;
using Microsoft.AspNetCore.Http;
using TaskExecution.Api.Endpoints;
using TaskExecution.Application.Tasks;
using TaskExecution.Domain;

namespace TaskExecution.UnitTests.Application;

public sealed class WorkAreaTaskCountTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handler_returns_completed_over_eligible_counts_and_zero_rows()
    {
        var populatedArea = Guid.NewGuid();
        var emptyArea = Guid.NewGuid();
        var reader = new FakeReader([
            new WorkAreaTaskCountValues(
                populatedArea,
                TotalTasks: 11,
                CancelledTasks: 1,
                CompletedTasks: 6,
                OverdueTasks: 2)
        ]);
        var handler = new GetWorkAreaTaskCountsQueryHandler(reader, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync([populatedArea, emptyArea]);

        result.Should().HaveCount(2);
        result[0].Should().Be(new WorkAreaTaskCountDto(
            populatedArea, 11, 1, 10, 6, 2, Now));
        result[1].Should().Be(new WorkAreaTaskCountDto(
            emptyArea, 0, 0, 0, 0, 0, Now));
        reader.Calls.Should().Be(1);
        reader.ReceivedIds.Should().Equal(populatedArea, emptyArea);
        reader.CalculatedAt.Should().Be(Now);
    }

    [Fact]
    public void Query_parser_removes_duplicates_and_preserves_first_seen_order()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["workAreaIds"] = new([first.ToString(), second.ToString(), first.ToString()])
        });

        var result = InternalWorkAreaTaskCountQuery.Parse(query);

        result.Should().Equal(first, second);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Query_parser_rejects_missing_or_invalid_ids(string value)
    {
        var values = string.IsNullOrEmpty(value)
            ? new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()
            : new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["workAreaIds"] = value
            };

        var act = () => InternalWorkAreaTaskCountQuery.Parse(new QueryCollection(values));

        act.Should().Throw<DomainRuleException>()
            .Which.Code.Should().Be("INVALID_WORK_AREA_IDS");
    }

    private sealed class FakeReader(IReadOnlyList<WorkAreaTaskCountValues> rows)
        : IWorkAreaTaskCountReader
    {
        public int Calls { get; private set; }
        public IReadOnlyCollection<Guid> ReceivedIds { get; private set; } = [];
        public DateTimeOffset CalculatedAt { get; private set; }

        public Task<IReadOnlyList<WorkAreaTaskCountValues>> GetAsync(
            IReadOnlyCollection<Guid> workAreaIds,
            DateTimeOffset calculatedAt,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            ReceivedIds = workAreaIds;
            CalculatedAt = calculatedAt;
            return Task.FromResult(rows);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
