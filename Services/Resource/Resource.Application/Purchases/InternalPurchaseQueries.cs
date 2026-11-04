using Resource.Contracts.Purchases;
using Resource.Domain;

namespace Resource.Application.Purchases;

public interface IInternalPurchaseReadStore
{
    Task<InternalPurchaseRequestSnapshot?> FindAsync(Guid id, CancellationToken ct);
    Task<InternalPurchaseRequestPage> ListAsync(Guid? afterId, int limit, CancellationToken ct);
    Task<InternalTaskMaterialSummary?> MaterialSummaryAsync(Guid taskId, CancellationToken ct);
}

public sealed class InternalPurchaseQueries(IInternalPurchaseReadStore store)
{
    public async Task<InternalPurchaseRequestSnapshot> GetAsync(Guid id, CancellationToken ct) =>
        await store.FindAsync(id, ct) ?? throw DomainRuleException.NotFound("PURCHASE_REQUEST_NOT_FOUND", "The purchase request does not exist.");

    public Task<InternalPurchaseRequestPage> ListAsync(Guid? afterId, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 200) throw DomainRuleException.Validation("PAGE_SIZE_INVALID", "Limit must be between 1 and 200.");
        return store.ListAsync(afterId, limit, ct);
    }

    public async Task<InternalTaskMaterialSummary> MaterialsAsync(Guid taskId, CancellationToken ct) =>
        await store.MaterialSummaryAsync(taskId, ct) ?? throw DomainRuleException.NotFound("TASK_NOT_PROJECTED", "Task ownership has not been projected; reconcile the task snapshot first.");
}
