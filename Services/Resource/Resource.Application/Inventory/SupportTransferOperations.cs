using Resource.Contracts.Inventory;
using Resource.Domain;
using Resource.Domain.Inventory;

namespace Resource.Application.Inventory;

public interface ISupportTransferOperationStore
{
    Task<T> InOperationTransactionAsync<T>(Guid id, Func<Task<T>> action, CancellationToken ct);
    Task<SupportTransferOperation?> GetAsync(Guid id, CancellationToken ct);
    void Add(SupportTransferOperation operation);
    Task SaveAsync(CancellationToken ct);
}

public interface ISupportTransferStarter
{
    Task<(SupportTransferDto Transfer, bool Created)> HandleAsync(StartSupportTransferCommand command, CancellationToken cancellationToken = default);
}

public sealed class SupportTransferOperationHandler(ISupportTransferOperationStore store, ISupportTransferStarter transfers, TimeProvider clock)
{
    public Task<SupportTransferOperationResult> HandleAsync(Guid id, StartSupportTransferCommand command, CancellationToken ct = default) =>
        store.InOperationTransactionAsync(id, async () =>
        {
            var operation = await store.GetAsync(id, ct);
            if (operation is not null)
            {
                operation.EnsureMatches(command.SupportResponseMaterialId, command.SourceWorkAreaId, command.TargetWorkAreaId,
                    command.MaterialId, command.Quantity, command.ApprovedByUserId);
                return Map(operation);
            }

            // Validate before executing any withdrawal. A missing outcome must remain ambiguous to callers.
            operation = SupportTransferOperation.Decide(id, command.SupportResponseMaterialId, command.SourceWorkAreaId,
                command.TargetWorkAreaId, command.MaterialId, command.Quantity, command.ApprovedByUserId, null, clock.GetUtcNow());
            try
            {
                await transfers.HandleAsync(command, ct);
            }
            catch (DomainRuleException exception) when (IsTerminalPreWriteRejection(exception.Code))
            {
                // These guards run before ledger/outbox mutations. DB, publication and transient projection errors roll back.
                operation = SupportTransferOperation.Decide(id, command.SupportResponseMaterialId, command.SourceWorkAreaId,
                    command.TargetWorkAreaId, command.MaterialId, command.Quantity, command.ApprovedByUserId, exception.Code, clock.GetUtcNow());
            }
            store.Add(operation);
            await store.SaveAsync(ct);
            return Map(operation);
        }, ct);

    public async Task<SupportTransferOperationResult?> GetAsync(Guid id, CancellationToken ct = default) =>
        await store.GetAsync(id, ct) is { } row ? Map(row) : null;

    private static bool IsTerminalPreWriteRejection(string code) => code is
        "MATERIAL_INSUFFICIENT_AVAILABLE" or "SUPPORT_TRANSFER_CONFLICT" or "SUPPORT_TRANSFER_OTHER_SEASON" or
        "SUPPORT_TRANSFER_SAME_WORK_AREA" or "SEASON_READ_ONLY";

    private static SupportTransferOperationResult Map(SupportTransferOperation row) => new(row.Id, row.SupportResponseMaterialId,
        row.SourceWorkAreaId, row.TargetWorkAreaId, row.MaterialId, row.Quantity, row.ApprovedByUserId, row.Status, row.ErrorCode, row.DecidedAt);
}
