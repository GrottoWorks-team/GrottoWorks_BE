namespace Resource.Domain.Inventory;

/// <summary>
/// Stock figures of one material in one work area (BA 8.5, API Contract 7.1). Pure value object so the
/// formula is unit-tested once and reused by inventory, shortage, purchase, allocation and support.
/// </summary>
public sealed record InventoryFigures(
    decimal Required,
    decimal ConfirmedAvailable,
    decimal Allocated,
    decimal Unallocated,
    decimal Shortage);

public static class InventoryCalculator
{
    /// <param name="required">Sum of ACTIVE requirements (0 when the material is not required).</param>
    /// <param name="ledgerTotal">SUM(transaction_quantity) of the ledger — confirmed available quantity.</param>
    /// <param name="activeAllocated">Allocated quantity (F-RES-15); usable task returns come back through the ledger.</param>
    /// <param name="taskReturned">Part of <paramref name="ledgerTotal"/> posted as <c>TASK_RETURN</c>. Allocations are never
    /// released (BR-51), so a verified remainder is stock the work area already received once: it frees unallocated stock
    /// but must not cover the requirement a second time.</param>
    public static InventoryFigures Calculate(decimal required, decimal ledgerTotal, decimal activeAllocated = 0, decimal taskReturned = 0)
    {
        if (required < 0 || activeAllocated < 0 || taskReturned < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(required), "Required, allocated and returned quantities are never negative.");
        }

        // The ledger never goes below zero through the commands of this service (outflows are checked against
        // unallocated stock); clamp defensively so reports never show negative stock.
        var confirmedAvailable = Math.Max(0, ledgerTotal);
        var receivedForRequirement = Math.Max(0, ledgerTotal - taskReturned);

        return new InventoryFigures(
            required,
            confirmedAvailable,
            activeAllocated,
            Math.Max(0, confirmedAvailable - activeAllocated),
            Math.Max(0, required - receivedForRequirement)); // BR-25: shortage never negative.
    }
}
