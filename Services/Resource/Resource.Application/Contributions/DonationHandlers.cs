using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Application.Inventory;
using Resource.Application.Purchases;
using Resource.Application.Scope;
using Resource.Application.Shortages;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Catalog;
using Resource.Domain.Contributions;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.Application.Contributions;

public sealed record DonationItemDto(
    Guid Id,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal? PledgedQuantity,
    decimal ReceivedQuantity,
    decimal UnusableQuantity,
    decimal UsableQuantity,
    DonationItemStatus Status,
    string? Note);

public sealed record DonationReceiptItemDto(
    Guid Id,
    Guid DonationItemId,
    Guid MaterialId,
    decimal ReceivedQuantity,
    decimal UnusableQuantity,
    decimal UsableQuantity);

public sealed record DonationReceiptDto(
    Guid Id,
    DateTimeOffset ReceivedAt,
    Guid ReceivedBy,
    MoneyDto? ReceivedAmount,
    string? Note,
    IReadOnlyList<DonationReceiptItemDto> Items);

/// <summary><c>Donation</c> (API Contract v1.3.2). <see cref="Receipts"/> is filled by getDonation only.</summary>
public sealed record DonationDto(
    Guid Id,
    Guid DonorId,
    string DonorName,
    Guid SeasonId,
    Guid? WorkAreaId,
    DonationType Type,
    DonationStatus Status,
    MoneyDto? PledgedAmount,
    MoneyDto? ReceivedAmount,
    DateTimeOffset DonationDate,
    IReadOnlyList<DonationItemDto> Items,
    IReadOnlyList<DonationReceiptDto>? Receipts,
    string? Note,
    string? CancelReason,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    long Version)
{
    public static DonationDto From(Donation donation, Donor donor, IReadOnlyDictionary<Guid, Material> materials, bool withReceipts)
    {
        var materialOfItem = donation.Items.ToDictionary(item => item.DonationItemId, item => item.MaterialId);
        return new DonationDto(
            donation.DonationId, donation.DonorId, donor.DonorName, donation.SeasonId, donation.WorkAreaId, donation.Type, donation.Status,
            donation.PledgedAmount is { } pledged ? MoneyDto.InVnd(pledged) : null,
            donation.Amount is { } amount ? MoneyDto.InVnd(amount) : null,
            donation.DonationDate,
            donation.Items
                .Select(item =>
                {
                    var material = materials[item.MaterialId];
                    return new DonationItemDto(item.DonationItemId, item.MaterialId, material.Code, material.Name, material.Unit,
                        item.PledgedQuantity, item.ReceivedQuantity, item.UnusableQuantity, item.UsableQuantity,
                        item.StatusIn(donation.Status), item.Note);
                })
                .OrderBy(item => item.MaterialName, StringComparer.CurrentCulture)
                .ToList(),
            withReceipts
                ? donation.Receipts
                    .OrderBy(receipt => receipt.ReceivedAt)
                    .Select(receipt => new DonationReceiptDto(
                        receipt.DonationReceiptId, receipt.ReceivedAt, receipt.ReceivedByUserId,
                        receipt.ReceivedAmount is { } received ? MoneyDto.InVnd(received) : null, receipt.Note,
                        receipt.Items
                            .Select(line => new DonationReceiptItemDto(line.DonationReceiptItemId, line.DonationItemId,
                                materialOfItem[line.DonationItemId], line.ReceivedQuantity, line.UnusableQuantity, line.UsableQuantity))
                            .ToList()))
                    .ToList()
                : null,
            donation.Note, donation.CancelReason, donation.CreatedByUserId, donation.CreatedAt, donation.UpdatedAt, donation.Version);
    }
}

public sealed record DonationFilter(
    int Page,
    int Size,
    Guid? SeasonId,
    Guid? WorkAreaId,
    Guid? DonorId,
    DonationType? Type,
    DonationStatus? Status);

public sealed record RecordDirectDonationCommand(
    Guid DonorId,
    DonationType Type,
    Guid? SeasonId,
    Guid? WorkAreaId,
    decimal? Amount,
    IReadOnlyList<ReceivedMaterialInput> Items,
    DateTimeOffset? ReceivedAt,
    string? Note);

/// <summary>
/// Which donations a material officer may record or change: MATERIAL (and MONEY with a work area) → the MO of that work
/// area; MONEY for the whole season → the MO of any work area of the season. Archived seasons are read-only (BR-07).
/// </summary>
public sealed class DonationOfficerScope(WorkAreaAccess access, IWorkAreaScopeStore scopes, ICurrentUser currentUser)
{
    public async Task<(WorkAreaScope? Scope, Guid SeasonId, Guid ParishId)> ResolveAsync(
        DonationType type,
        Guid? workAreaId,
        Guid? seasonId,
        CancellationToken cancellationToken)
    {
        if (workAreaId is { } areaId)
        {
            var scope = await access.ForMaterialOfficerWriteAsync(areaId, cancellationToken);
            if (seasonId is { } season && season != scope.SeasonId)
            {
                throw DomainRuleException.Validation("DONATION_SEASON_MISMATCH", "The work area belongs to another season.");
            }

            return (scope, scope.SeasonId, scope.ParishId);
        }

        if (type == DonationType.Material)
        {
            throw DomainRuleException.Validation("DONATION_WORK_AREA_REQUIRED", "A MATERIAL donation needs the receiving work area (BR-37).");
        }

        var requiredSeason = seasonId
            ?? throw DomainRuleException.Validation("DONATION_SEASON_REQUIRED", "Choose the season (or a work area) the money is for.");
        var parishId = access.RequireParish();
        var areas = await scopes.ListByParishAsync(parishId, requiredSeason, cancellationToken);
        if (areas.Count == 0)
        {
            throw DomainRuleException.NotFound("SEASON_NOT_FOUND", "The season does not exist or is outside your parish.");
        }

        if (!areas.Any(area => area.MaterialOfficerUserId == currentUser.UserId))
        {
            throw DomainRuleException.Forbidden("WORK_AREA_SCOPE_DENIED", "Only a material officer of this season can record its donations.");
        }

        WorkAreaAccess.EnsureWritable(areas[0]);
        return (null, requiredSeason, parishId);
    }
}

/// <summary>
/// Posts one receipt (BR-38): stages a <c>DONATION_RECEIPT</c> ledger row per line with usable quantity on the receiving
/// work area (reference <c>DONATION_RECEIPT_ITEM</c> → posted once), in the caller's unit of work; after the save,
/// publishes <c>material.shortage.changed</c> where it moved and <c>contribution.receipt.confirmed</c>.
/// </summary>
public sealed class DonationReceiptPoster(
    IInventoryStore inventory,
    ShortageTracker shortages,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<ShortageTracker.Snapshot>> StageAsync(
        WorkAreaScope? scope,
        Donation donation,
        DonationReceipt receipt,
        CancellationToken cancellationToken)
    {
        var before = new List<ShortageTracker.Snapshot>();
        if (scope is null)
        {
            return before;
        }

        var materialOfItem = donation.Items.ToDictionary(item => item.DonationItemId, item => item.MaterialId);
        foreach (var line in receipt.Items.Where(line => line.UsableQuantity > 0))
        {
            var materialId = materialOfItem[line.DonationItemId];
            before.Add(await shortages.CaptureAsync(scope, materialId, cancellationToken));
            inventory.Append(MaterialTransaction.Post(
                scope.WorkAreaId, materialId, TransactionType.DonationReceipt, line.UsableQuantity, receipt.ReceivedAt,
                currentUser.UserId, null, TransactionReferenceType.DonationReceiptItem, line.DonationReceiptItemId));
        }

        return before;
    }

    public async Task PublishAsync(
        IReadOnlyList<ShortageTracker.Snapshot> before,
        Donation donation,
        DonationReceipt receipt,
        Donor donor,
        Guid parishId,
        bool isDirect,
        bool isFirstReceipt,
        CancellationToken cancellationToken)
    {
        foreach (var snapshot in before)
        {
            await shortages.PublishIfChangedAsync(snapshot, cancellationToken);
        }

        await publisher.PublishAsync(
            ContributionReceiptConfirmed.EventType,
            ContributionReceiptConfirmed.Version,
            DonationEvents.ReceiptConfirmed(donation, receipt, donor, parishId, isDirect, isFirstReceipt),
            "donation",
            donation.DonationId,
            cancellationToken);
    }
}

/// <summary>Builds <see cref="DonationDto"/>s: donors and catalog fields in two batched reads.</summary>
public sealed class DonationProjector(IDonorStore donors, IMaterialCatalogStore catalog)
{
    public async Task<IReadOnlyList<DonationDto>> ToDtosAsync(
        IReadOnlyCollection<Donation> donations,
        bool withReceipts,
        CancellationToken cancellationToken)
    {
        var donorById = (await donors.GetManyAsync(donations.Select(d => d.DonorId).Distinct().ToList(), cancellationToken))
            .ToDictionary(donor => donor.DonorId);
        var materials = (await catalog.GetMaterialsAsync(
                donations.SelectMany(d => d.Items).Select(item => item.MaterialId).Distinct().ToList(), cancellationToken))
            .ToDictionary(material => material.MaterialId);
        return donations.Select(d => DonationDto.From(d, donorById[d.DonorId], materials, withReceipts)).ToList();
    }

    public async Task<DonationDto> ToDtoAsync(Donation donation, CancellationToken cancellationToken) =>
        (await ToDtosAsync([donation], withReceipts: true, cancellationToken))[0];
}

/// <summary>
/// F-RES-13 <c>recordDirectDonation</c> (MO): a donation already received — donation RECEIVED + its first receipt, the
/// usable quantity of every line posted to the receiving work area's ledger (<c>DONATION_RECEIPT</c>, reference
/// <c>DONATION_RECEIPT_ITEM</c>), all in one SaveChanges = one transaction (BR-38). MATERIAL: MO of the receiving work
/// area. MONEY: MO of that work area when given, otherwise MO of any work area of the season.
/// </summary>
public sealed class RecordDirectDonationCommandHandler(
    DonationOfficerScope officers,
    IDonorStore donors,
    IDonationStore store,
    IMaterialCatalogStore catalog,
    DonationReceiptPoster poster,
    DonationProjector projector,
    ICurrentUser currentUser)
{
    public async Task<DonationDto> HandleAsync(RecordDirectDonationCommand command, CancellationToken cancellationToken = default)
    {
        var (scope, seasonId, parishId) = await officers.ResolveAsync(command.Type, command.WorkAreaId, command.SeasonId, cancellationToken);

        var donor = await donors.GetAsync(command.DonorId, cancellationToken);
        if (donor is null || donor.ParishId != parishId)
        {
            throw DomainRuleException.Validation("DONOR_UNKNOWN", "The donor does not exist in this parish. Create the donor first.");
        }

        var now = DateTimeOffset.UtcNow;
        var donation = Donation.RecordDirect(command.DonorId, seasonId, command.WorkAreaId, command.Type, command.Amount, command.Items,
            command.ReceivedAt ?? now, command.Note, currentUser.UserId, now);
        await MaterialRules.EnsureActiveAsync(catalog, donation.Items.Select(item => item.MaterialId), cancellationToken);

        var receipt = donation.Receipts.Single();
        var posting = await poster.StageAsync(scope, donation, receipt, cancellationToken);

        store.Add(donation);
        await store.SaveChangesAsync(cancellationToken);

        await poster.PublishAsync(posting, donation, receipt, donor, parishId, isDirect: true, isFirstReceipt: true, cancellationToken);
        return await projector.ToDtoAsync(donation, cancellationToken);
    }
}

/// <summary>F-RES-13 <c>listDonations</c> (PARISH, LEADER, MO): donations of the caller's parish, newest first.</summary>
public sealed class ListDonationsQueryHandler(
    WorkAreaAccess access,
    IWorkAreaScopeStore scopes,
    IDonationStore store,
    DonationProjector projector)
{
    public async Task<PagedResult<DonationDto>> HandleAsync(DonationFilter filter, CancellationToken cancellationToken = default)
    {
        var parishId = access.RequireParish();
        var seasonIds = (await scopes.ListByParishAsync(parishId, filter.SeasonId, cancellationToken))
            .Select(area => area.SeasonId)
            .Distinct()
            .ToList();
        if (seasonIds.Count == 0)
        {
            return new PagedResult<DonationDto>([], 0);
        }

        var page = await store.SearchAsync(
            new DonationSearch(seasonIds, filter.WorkAreaId, filter.DonorId, filter.Type, filter.Status, filter.Page, filter.Size),
            cancellationToken);
        return new PagedResult<DonationDto>(await projector.ToDtosAsync(page.Items, withReceipts: false, cancellationToken), page.TotalItems);
    }
}

/// <summary>
/// F-RES-13 <c>getDonation</c> (MO of the parish; VOLUNTEER only for a donation of the donor linked to their account):
/// donation with items and receipts. Anything else is 404.
/// </summary>
public sealed class GetDonationQueryHandler(
    WorkAreaAccess access,
    IWorkAreaScopeStore scopes,
    IDonationStore store,
    IDonorStore donors,
    DonationProjector projector,
    ICurrentUser currentUser)
{
    public async Task<DonationDto> HandleAsync(Guid donationId, CancellationToken cancellationToken = default)
    {
        var donation = await store.GetAsync(donationId, cancellationToken) ?? throw NotFound();

        if (currentUser.Role == GrottoWorksRoles.Volunteer)
        {
            var donor = await donors.GetAsync(donation.DonorId, cancellationToken);
            if (donor?.UserId != currentUser.UserId)
            {
                throw NotFound();
            }
        }
        else
        {
            var parishId = access.RequireParish();
            if ((await scopes.ListByParishAsync(parishId, donation.SeasonId, cancellationToken)).Count == 0)
            {
                throw NotFound();
            }
        }

        return await projector.ToDtoAsync(donation, cancellationToken);
    }

    private static DomainRuleException NotFound() => DomainRuleException.NotFound("DONATION_NOT_FOUND", "The donation does not exist.");
}

/// <summary>
/// F-RES-13 <c>listMyContributions</c> (VOLUNTEER): donations of the donor linked to the caller's account, newest first.
/// Contract v1.3.2 names the item <c>Contribution</c> without a schema; until Reporting owns contributions (F-RPT-02)
/// this returns the donation view. No linked donor → empty page.
/// </summary>
public sealed class ListMyContributionsQueryHandler(
    IDonorStore donors,
    IDonationStore store,
    DonationProjector projector,
    ICurrentUser currentUser)
{
    public async Task<PagedResult<DonationDto>> HandleAsync(
        int page,
        int size,
        Guid? seasonId,
        CancellationToken cancellationToken = default)
    {
        var donor = await donors.FindByUserAsync(currentUser.UserId, cancellationToken);
        if (donor is null)
        {
            return new PagedResult<DonationDto>([], 0);
        }

        var result = await store.SearchAsync(
            new DonationSearch(seasonId is { } season ? [season] : null, null, donor.DonorId, null, null, page, size),
            cancellationToken);
        return new PagedResult<DonationDto>(await projector.ToDtosAsync(result.Items, withReceipts: false, cancellationToken), result.TotalItems);
    }
}

/// <summary>Result of <c>recordDonationReceipt</c>: the receipt, the donation status after it and non-blocking warnings.</summary>
public sealed record RecordedReceiptDto(
    Guid Id,
    Guid DonationId,
    DateTimeOffset ReceivedAt,
    Guid ReceivedBy,
    MoneyDto? ReceivedAmount,
    string? Note,
    IReadOnlyList<DonationReceiptItemDto> Items,
    DonationStatus DonationStatus,
    IReadOnlyList<string> Warnings);

public sealed record RecordDonationReceiptCommand(
    Guid DonationId,
    DateTimeOffset? ReceivedAt,
    decimal? Amount,
    IReadOnlyList<ReceiptLineInput> Items,
    string? Note);

/// <summary>
/// F-RES-13c <c>recordDonationReceipt</c> (MO of the receiving work area / season): one actual receipt of a pledge —
/// cumulated totals, PARTIALLY_RECEIVED → RECEIVED, usable quantity posted once per receipt line, over-receipt only
/// warned (BA 8.7). The donation row is updated too, so two concurrent receipts cannot both win (xmin → 412).
/// </summary>
public sealed class RecordDonationReceiptCommandHandler(
    DonationOfficerScope officers,
    IDonationStore store,
    IDonorStore donors,
    DonationReceiptPoster poster,
    ICurrentUser currentUser)
{
    public async Task<RecordedReceiptDto> HandleAsync(RecordDonationReceiptCommand command, CancellationToken cancellationToken = default)
    {
        var donation = await store.GetAsync(command.DonationId, cancellationToken)
            ?? throw DomainRuleException.NotFound("DONATION_NOT_FOUND", "The donation does not exist.");
        WorkAreaScope? scope;
        Guid parishId;
        try
        {
            (scope, _, parishId) = await officers.ResolveAsync(donation.Type, donation.WorkAreaId, donation.SeasonId, cancellationToken);
        }
        catch (DomainRuleException exception) when (exception.Kind == DomainErrorKind.NotFound)
        {
            throw DomainRuleException.NotFound("DONATION_NOT_FOUND", "The donation does not exist.");
        }

        var now = DateTimeOffset.UtcNow;
        var recorded = donation.RecordReceipt(command.ReceivedAt ?? now, command.Amount, command.Items, command.Note, currentUser.UserId, now);
        var posting = await poster.StageAsync(scope, donation, recorded.Receipt, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        var donor = await donors.GetAsync(donation.DonorId, cancellationToken)
            ?? throw new InvalidOperationException("Donation points to a missing donor.");
        await poster.PublishAsync(posting, donation, recorded.Receipt, donor, parishId, isDirect: false, recorded.IsFirstReceipt, cancellationToken);

        var materialOfItem = donation.Items.ToDictionary(item => item.DonationItemId, item => item.MaterialId);
        var receipt = recorded.Receipt;
        return new RecordedReceiptDto(
            receipt.DonationReceiptId, donation.DonationId, receipt.ReceivedAt, receipt.ReceivedByUserId,
            receipt.ReceivedAmount is { } amount ? MoneyDto.InVnd(amount) : null, receipt.Note,
            receipt.Items.Select(line => new DonationReceiptItemDto(line.DonationReceiptItemId, line.DonationItemId,
                materialOfItem[line.DonationItemId], line.ReceivedQuantity, line.UnusableQuantity, line.UsableQuantity)).ToList(),
            donation.Status, recorded.Warnings);
    }
}

internal static class DonationEvents
{
    public static ContributionReceiptConfirmed ReceiptConfirmed(
        Donation donation,
        DonationReceipt receipt,
        Donor donor,
        Guid parishId,
        bool isDirect,
        bool isFirstReceipt)
    {
        var materialOfItem = donation.Items.ToDictionary(item => item.DonationItemId, item => item.MaterialId);
        return new ContributionReceiptConfirmed(
            donation.DonationId, receipt.DonationReceiptId, donation.DonorId, donor.UserId, parishId, donation.SeasonId,
            donation.WorkAreaId, donation.Type == DonationType.Money ? "MONEY" : "MATERIAL", receipt.ReceivedAmount, MoneyDto.Vnd,
            receipt.Items
                .Select(line => new ContributionReceiptLine(line.DonationItemId, materialOfItem[line.DonationItemId],
                    line.ReceivedQuantity, line.UnusableQuantity, line.UsableQuantity))
                .ToList(),
            isDirect, isFirstReceipt, receipt.ReceivedByUserId, receipt.ReceivedAt);
    }
}
