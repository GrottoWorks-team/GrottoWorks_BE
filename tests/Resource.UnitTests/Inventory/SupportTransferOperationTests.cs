using Resource.Application.Inventory;
using Resource.Domain;
using Resource.Domain.Inventory;

namespace Resource.UnitTests.Inventory;

public sealed class SupportTransferOperationTests
{
    [Fact]
    public async Task Insufficient_stock_is_a_durable_decision_even_if_stock_is_replenished_later()
    {
        var store = new Store(); var starter = new Starter { Error = new DomainRuleException("MATERIAL_INSUFFICIENT_AVAILABLE", "short") };
        var handler = new SupportTransferOperationHandler(store, starter, TimeProvider.System);
        var id = Guid.NewGuid(); var command = Command();
        var first = await handler.HandleAsync(id, command);
        starter.Error = null;
        var replay = await handler.HandleAsync(id, command);
        first.Status.Should().Be("REJECTED"); replay.Should().Be(first);
        starter.Calls.Should().Be(1); store.Commits.Should().Be(2);
    }

    [Fact]
    public async Task Successful_replay_does_not_execute_another_withdrawal()
    {
        var store = new Store(); var starter = new Starter();
        var handler = new SupportTransferOperationHandler(store, starter, TimeProvider.System);
        var id = Guid.NewGuid(); var command = Command();
        (await handler.HandleAsync(id, command)).Status.Should().Be("COMPLETED");
        await handler.HandleAsync(id, command);
        starter.Calls.Should().Be(1);
        Func<Task> changed = () => handler.HandleAsync(id, command with { Quantity = 2 });
        (await changed.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("SUPPORT_TRANSFER_OPERATION_CONFLICT");
    }

    [Fact]
    public async Task Unknown_projection_or_storage_failure_does_not_become_a_terminal_rejection()
    {
        var store = new Store(); var starter = new Starter { Error = DomainRuleException.Validation("WORK_AREA_NOT_PROJECTED", "missing") };
        var handler = new SupportTransferOperationHandler(store, starter, TimeProvider.System);
        Func<Task> action = () => handler.HandleAsync(Guid.NewGuid(), Command());
        await action.Should().ThrowAsync<DomainRuleException>();
        store.Rows.Should().BeEmpty(); store.Commits.Should().Be(0);
        starter.Error = new TimeoutException();
        await action.Should().ThrowAsync<TimeoutException>();
        store.Rows.Should().BeEmpty(); store.Commits.Should().Be(0);
    }

    [Fact]
    public async Task Different_attempt_can_retry_after_a_definitive_rejection()
    {
        var store = new Store(); var starter = new Starter { Error = new DomainRuleException("MATERIAL_INSUFFICIENT_AVAILABLE", "short") };
        var handler = new SupportTransferOperationHandler(store, starter, TimeProvider.System); var command = Command();
        await handler.HandleAsync(Guid.NewGuid(), command);
        starter.Error = null;
        (await handler.HandleAsync(Guid.NewGuid(), command)).Status.Should().Be("COMPLETED");
        store.Rows.Should().HaveCount(2);
    }

    private static StartSupportTransferCommand Command() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid());
    private sealed class Starter : ISupportTransferStarter
    {
        public Exception? Error; public int Calls;
        public Task<(SupportTransferDto Transfer, bool Created)> HandleAsync(StartSupportTransferCommand command, CancellationToken cancellationToken = default)
        {
            Calls++; if (Error is not null) throw Error;
            return Task.FromResult((SupportTransferDto.From(SupportTransfer.Start(command.SupportResponseMaterialId, command.SourceWorkAreaId,
                command.TargetWorkAreaId, command.MaterialId, command.Quantity, command.ApprovedByUserId, DateTimeOffset.UtcNow)), true));
        }
    }
    private sealed class Store : ISupportTransferOperationStore
    {
        public readonly List<SupportTransferOperation> Rows = []; public int Commits;
        public async Task<T> InOperationTransactionAsync<T>(Guid id, Func<Task<T>> action, CancellationToken ct)
        { var result = await action(); Commits++; return result; }
        public Task<SupportTransferOperation?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(Rows.SingleOrDefault(row => row.Id == id));
        public void Add(SupportTransferOperation operation) => Rows.Add(operation);
        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
