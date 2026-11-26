using Microsoft.EntityFrameworkCore;
using Resource.Application.Inventory;
using Resource.Domain;
using Resource.Domain.Inventory;
using Resource.Infrastructure.Data;
using Resource.Infrastructure.Data.Configurations;

namespace Resource.Infrastructure.Inventory;

/// <summary>Append-only parish warehouse ledger (F-RES-21).</summary>
public sealed class ParishInventoryStore(ResourceDbContext dbContext) : IParishInventoryStore
{
    public void Append(ParishMaterialTransaction transaction) => dbContext.ParishMaterialTransactions.Add(transaction);

    public Task<bool> OpeningStockExistsAsync(
        Guid parishId,
        Guid seasonId,
        Guid materialId,
        CancellationToken cancellationToken = default) =>
        dbContext.ParishMaterialTransactions.AnyAsync(
            transaction => transaction.ParishId == parishId &&
                           transaction.SeasonId == seasonId &&
                           transaction.MaterialId == materialId &&
                           transaction.Type == TransactionType.OpeningStock,
            cancellationToken);

    public async Task<IReadOnlyList<ParishQuantityByMaterial>> GetTotalsAsync(
        Guid parishId,
        Guid seasonId,
        Guid? materialId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.ParishMaterialTransactions.AsNoTracking()
            .Where(transaction => transaction.ParishId == parishId && transaction.SeasonId == seasonId);
        if (materialId is { } material)
        {
            query = query.Where(transaction => transaction.MaterialId == material);
        }

        return await query
            .GroupBy(transaction => transaction.MaterialId)
            .Select(group => new ParishQuantityByMaterial(group.Key, group.Sum(transaction => transaction.Quantity)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ParishMaterialTransaction>> ListTransactionsAsync(
        ParishTransactionQuery query,
        CancellationToken cancellationToken = default)
    {
        var transactions = dbContext.ParishMaterialTransactions.AsNoTracking()
            .Where(transaction => transaction.ParishId == query.ParishId &&
                                  transaction.SeasonId == query.SeasonId &&
                                  transaction.MaterialId == query.MaterialId);
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
            .ThenBy(transaction => transaction.ParishMaterialTransactionId)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(ParishMaterialTransactionConfiguration.OpeningStockIndex))
        {
            throw new DomainRuleException(
                "PARISH_OPENING_STOCK_ALREADY_RECORDED",
                "Opening stock was already recorded for this material in this season. Use a parish inventory adjustment.");
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(ParishMaterialTransactionConfiguration.ReferenceIndex))
        {
            throw new DomainRuleException(
                "TRANSACTION_ALREADY_POSTED",
                "This source record was already posted to the parish warehouse ledger.");
        }
    }
}
