using Microsoft.EntityFrameworkCore;
using Resource.Application.Inventory;
using Resource.Domain;
using Resource.Domain.Inventory;
using Resource.Infrastructure.Data;
using Resource.Infrastructure.Data.Configurations;

namespace Resource.Infrastructure.Inventory;

public sealed class SupportTransferStore(ResourceDbContext dbContext) : ISupportTransferStore
{
    public Task<SupportTransfer?> GetAsync(Guid supportResponseMaterialId, CancellationToken cancellationToken = default) =>
        dbContext.SupportTransfers.FirstOrDefaultAsync(transfer => transfer.SupportResponseMaterialId == supportResponseMaterialId, cancellationToken);

    public void Add(SupportTransfer transfer) => dbContext.SupportTransfers.Add(transfer);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(MaterialTransactionConfiguration.ReferenceIndex))
        {
            throw new DomainRuleException("SUPPORT_TRANSFER_ALREADY_POSTED", "This support transfer was already posted to stock.");
        }
        catch (DbUpdateConcurrencyException exception) when (exception.Entries.Any(entry => entry.Entity is SupportTransfer))
        {
            throw new DomainRuleException(
                "SUPPORT_TRANSFER_VERSION_MISMATCH",
                "The transfer was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }
}
