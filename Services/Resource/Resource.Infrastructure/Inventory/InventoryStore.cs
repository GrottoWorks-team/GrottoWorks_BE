using Microsoft.EntityFrameworkCore;
using Resource.Application.Inventory;
using Resource.Domain;
using Resource.Domain.Inventory;
using Resource.Infrastructure.Data;
using Resource.Infrastructure.Data.Configurations;

namespace Resource.Infrastructure.Inventory;

public sealed class InventoryStore(ResourceDbContext dbContext) : IInventoryStore
{
    public void Append(MaterialTransaction transaction) => dbContext.MaterialTransactions.Add(transaction);

    public Task<bool> OpeningStockExistsAsync(Guid workAreaId, Guid materialId, CancellationToken cancellationToken = default) =>
        dbContext.MaterialTransactions.AnyAsync(
            transaction => transaction.WorkAreaId == workAreaId &&
                           transaction.MaterialId == materialId &&
                           transaction.Type == TransactionType.OpeningStock,
            cancellationToken);

    public async Task<IReadOnlyList<QuantityByMaterial>> GetLedgerTotalsAsync(
        IReadOnlyCollection<Guid> workAreaIds,
        Guid? materialId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.MaterialTransactions.AsNoTracking().Where(transaction => workAreaIds.Contains(transaction.WorkAreaId));
        if (materialId is { } material)
        {
            query = query.Where(transaction => transaction.MaterialId == material);
        }

        return await query
            .GroupBy(transaction => new { transaction.WorkAreaId, transaction.MaterialId })
            .Select(group => new QuantityByMaterial(group.Key.WorkAreaId, group.Key.MaterialId, group.Sum(transaction => transaction.Quantity)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QuantityByMaterial>> GetRequiredTotalsAsync(
        IReadOnlyCollection<Guid> workAreaIds,
        Guid? materialId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.MaterialRequirements.AsNoTracking()
            .Where(requirement => workAreaIds.Contains(requirement.WorkAreaId) && requirement.Status == RequirementStatus.Active);
        if (materialId is { } material)
        {
            query = query.Where(requirement => requirement.MaterialId == material);
        }

        return await query
            .Select(requirement => new QuantityByMaterial(requirement.WorkAreaId, requirement.MaterialId, requirement.RequiredQuantity))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QuantityByMaterial>> GetAllocatedTotalsAsync(
        IReadOnlyCollection<Guid> workAreaIds,
        Guid? materialId,
        CancellationToken cancellationToken = default)
    {
        var query =
            from allocation in dbContext.MaterialAllocations.AsNoTracking()
            from item in allocation.Items
            where workAreaIds.Contains(allocation.WorkAreaId)
            select new { allocation.WorkAreaId, item.MaterialId, item.AllocatedQuantity };
        if (materialId is { } material)
        {
            query = query.Where(row => row.MaterialId == material);
        }

        return await query
            .GroupBy(row => new { row.WorkAreaId, row.MaterialId })
            .Select(group => new QuantityByMaterial(group.Key.WorkAreaId, group.Key.MaterialId, group.Sum(row => row.AllocatedQuantity)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MaterialTransaction>> ListTransactionsAsync(TransactionQuery query, CancellationToken cancellationToken = default)
    {
        var transactions = dbContext.MaterialTransactions.AsNoTracking()
            .Where(transaction => transaction.MaterialId == query.MaterialId && query.WorkAreaIds.Contains(transaction.WorkAreaId));

        if (query.From is { } from)
        {
            transactions = transactions.Where(transaction => transaction.TransactionDate >= from);
        }

        if (query.To is { } to)
        {
            transactions = transactions.Where(transaction => transaction.TransactionDate <= to);
        }

        return await transactions
            .OrderByDescending(transaction => transaction.TransactionDate)
            .ThenBy(transaction => transaction.MaterialTransactionId)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(MaterialTransactionConfiguration.OpeningStockIndex))
        {
            throw new DomainRuleException(
                "OPENING_STOCK_ALREADY_RECORDED",
                "Opening stock was already recorded for this material in this work area. Use an inventory adjustment.");
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(MaterialTransactionConfiguration.ReferenceIndex))
        {
            throw new DomainRuleException(
                "TRANSACTION_ALREADY_POSTED",
                "This source record was already posted to the stock ledger.");
        }
    }
}
