using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Application.Inventory;
using Resource.Application.Purchases;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Contributions;
using Resource.Domain.Inventory;

namespace Resource.Application.Contributions;

public sealed record AdjustDonationCommand(
    Guid DonationId,
    long? Version,
    Guid ReceiptId,
    decimal? Amount,
    IReadOnlyList<ReceiptLineCorrection> Items,
    string? Reason);

/// <summary>
/// F-RES-14 <c>adjustDonation</c> (MO of the receiving work area / season): corrects one receipt and reconciles in one
/// transaction — usable increase → <c>DONATION_RECEIPT</c>, decrease → <c>ADJUSTMENT_DISPOSED</c> (only from
/// unallocated stock, under the stock lock), both referencing the correction as <c>INVENTORY_ADJUSTMENT</c>; then
/// <c>contribution.donation.adjusted</c> so Reporting corrects the contribution. A reason is always required.
/// </summary>
public sealed class AdjustDonationCommandHandler(
    MaterialOfficerScope officers,
    IDonationStore store,
    IDonorStore donors,
    IMaterialCatalogStore catalog,
    InventoryReader reader,
    IInventoryStore inventory,
    IWorkAreaStockLock stockLock,
    ShortageTracker shortages,
    DonationProjector projector,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public const int MaxReasonLength = 500;

    public async Task<DonationDto> HandleAsync(AdjustDonationCommand command, CancellationToken cancellationToken = default)
    {
        var donation = await store.GetAsync(command.DonationId, cancellationToken) ?? throw NotFound();
        Domain.Scope.WorkAreaScope? scope;
        Guid parishId;
        try
        {
            (scope, _, parishId) = await officers.ResolveAsync(donation.Type, donation.WorkAreaId, donation.SeasonId, cancellationToken);
        }
        catch (DomainRuleException exception) when (exception.Kind == DomainErrorKind.NotFound)
        {
            throw NotFound();
        }

        var reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();
        if (reason is null || reason.Length > MaxReasonLength)
        {
            throw DomainRuleException.Validation("DONATION_ADJUSTMENT_REASON_REQUIRED", $"A reason of at most {MaxReasonLength} characters is required.");
        }

        donation.EnsureVersion(command.Version);
        var now = DateTimeOffset.UtcNow;
        var adjustment = donation.AdjustReceipt(command.ReceiptId, command.Amount, command.Items, now);

        var changed = adjustment.Lines.Where(line => line.UsableDelta != 0).ToList();
        var before = new List<ShortageTracker.Snapshot>();
        if (scope is not null && changed.Count > 0)
        {
            var materials = (await catalog.GetMaterialsAsync(changed.Select(line => line.MaterialId).Distinct().ToList(), cancellationToken))
                .ToDictionary(material => material.MaterialId);
            await using var lease = await stockLock.AcquireAsync(scope.WorkAreaId, cancellationToken);
            await StockGuard.EnsureUnallocatedAsync(reader, scope, materials,
                changed.Where(line => line.UsableDelta < 0).Select(line => (line.MaterialId, -line.UsableDelta)), cancellationToken);
            foreach (var line in changed)
            {
                before.Add(await shortages.CaptureAsync(scope, line.MaterialId, cancellationToken));
                inventory.Append(MaterialTransaction.Post(
                    scope.WorkAreaId, line.MaterialId,
                    line.UsableDelta > 0 ? TransactionType.DonationReceipt : TransactionType.AdjustmentDisposed,
                    Math.Abs(line.UsableDelta), now, currentUser.UserId,
                    $"Correction of donation receipt {adjustment.Receipt.DonationReceiptId}: {reason}",
                    TransactionReferenceType.InventoryAdjustment, Guid.NewGuid()));
            }

            await store.SaveChangesAsync(cancellationToken);
            await lease.CommitAsync(cancellationToken);
        }
        else
        {
            await store.SaveChangesAsync(cancellationToken);
        }

        foreach (var snapshot in before)
        {
            await shortages.PublishIfChangedAsync(snapshot, cancellationToken);
        }

        var donor = await donors.GetAsync(donation.DonorId, cancellationToken)
            ?? throw new InvalidOperationException("Donation points to a missing donor.");
        await publisher.PublishAsync(
            ContributionDonationAdjusted.EventType,
            ContributionDonationAdjusted.Version,
            new ContributionDonationAdjusted(
                donation.DonationId, adjustment.Receipt.DonationReceiptId, donation.DonorId, donor.UserId, parishId, donation.SeasonId,
                donation.WorkAreaId, donation.Type == DonationType.Money ? "MONEY" : "MATERIAL", adjustment.AmountDelta, MoneyDto.Vnd,
                adjustment.Lines.Select(line => new DonationAdjustmentLine(line.DonationItemId, line.MaterialId, line.ReceivedDelta,
                    line.UnusableDelta, line.UsableDelta)).ToList(),
                reason, currentUser.UserId, now),
            "donation",
            donation.DonationId,
            cancellationToken);
        return await projector.ToDtoAsync(donation, cancellationToken);
    }

    private static DomainRuleException NotFound() => DomainRuleException.NotFound("DONATION_NOT_FOUND", "The donation does not exist.");
}
