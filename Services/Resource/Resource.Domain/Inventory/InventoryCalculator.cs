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
    public static InventoryFigures Calculate(decimal required, decimal ledgerTotal, decimal activeAllocated = 0)
    {
        if (required < 0 || activeAllocated < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(required), "Required and allocated quantities are never negative.");
        }

        // The ledger never goes below zero through the commands of this service (outflows are checked against
        // unallocated stock); clamp defensively so reports never show negative stock.
        var confirmedAvailable = Math.Max(0, ledgerTotal);

        return new InventoryFigures(
            required,
            confirmedAvailable,
            activeAllocated,
            Math.Max(0, confirmedAvailable - activeAllocated),
            Math.Max(0, required - confirmedAvailable)); // BR-25: shortage never negative.
    }
}
