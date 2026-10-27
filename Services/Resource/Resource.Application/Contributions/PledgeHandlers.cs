using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Catalog;
using Resource.Application.Inventory;
using Resource.Application.Purchases;
using Resource.Application.Requirements;
using Resource.Application.Scope;
using Resource.Application.Users;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Contributions;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;

namespace Resource.Application.Contributions;

/// <summary>
/// <c>ContributionNeed</c>: a material still missing in a work area. <see cref="RemainingNeed"/> = shortage minus what
/// volunteers already pledged and have not delivered. Monetary needs need the season budget (ParishCoordination) and are
/// not listed yet.
/// </summary>
public sealed record ContributionNeedDto(
    string Kind,
    Guid WorkAreaId,
    string WorkAreaName,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal Required,
    decimal Shortage,
    decimal PledgedOutstanding,
    decimal RemainingNeed);

public sealed record PledgeMaterialCommand(Guid WorkAreaId, IReadOnlyList<PledgedMaterialInput> Items, string? Note);

public sealed record PledgeMoneyCommand(Guid SeasonId, Guid? WorkAreaId, decimal Amount, string? Note);

public sealed record CancelPledgeCommand(Guid DonationId, long? Version, string? Reason);

/// <summary>F-RES-13b <c>listContributionNeeds</c> (USER of the parish): shortages of the season with what is still open after pledges.</summary>
public sealed class ListContributionNeedsQueryHandler(
    WorkAreaAccess access,
    IWorkAreaScopeStore scopes,
    InventoryReader reader,
    IDonationStore donations)
{
    public async Task<PagedResult<ContributionNeedDto>> HandleAsync(Shortages.MaterialNeedFilter filter, CancellationToken cancellationToken = default)
    {
        var areas = (await scopes.ListByParishAsync(access.RequireParish(), filter.SeasonId, cancellationToken))
            .Where(area => filter.WorkAreaId is null || area.WorkAreaId == filter.WorkAreaId)
            .ToList();
        if (areas.Count == 0)
        {
            return new PagedResult<ContributionNeedDto>([], 0);
        }

        var pledged = (await donations.GetOutstandingPledgesAsync(areas.Select(area => area.WorkAreaId).ToList(), cancellationToken))
            .ToDictionary(row => (row.WorkAreaId, row.MaterialId), row => row.Quantity);
        var needs = (await reader.ReadAsync(areas, filter.MaterialId, cancellationToken))
            .Where(row => row.Shortage > 0)
            .Select(row =>
            {
                var outstanding = pledged.GetValueOrDefault((row.WorkAreaId, row.MaterialId));
                return new ContributionNeedDto("MATERIAL", row.WorkAreaId, row.WorkAreaName, row.MaterialId, row.MaterialCode, row.MaterialName,
                    row.Unit, row.Required, row.Shortage, outstanding, Math.Max(0, row.Shortage - outstanding));
            })
            .OrderByDescending(need => need.RemainingNeed)
            .ThenBy(need => need.WorkAreaName, StringComparer.CurrentCulture)
            .ThenBy(need => need.MaterialName, StringComparer.CurrentCulture)
            .ToList();

        return new PagedResult<ContributionNeedDto>(needs.Skip((filter.Page - 1) * filter.Size).Take(filter.Size).ToList(), needs.Count);
    }
}

/// <summary>
/// BA 8.7 step 3: the donor linked to the caller's account, created on the first pledge (name from Identity; when
/// Identity is down a placeholder the officer can rename).
/// </summary>
public sealed class VolunteerDonorResolver(IDonorStore donors, IIdentityUserLookup identity, ICurrentUser currentUser)
{
    public async Task<Donor> ResolveAsync(Guid parishId, CancellationToken cancellationToken)
    {
        if (await donors.FindByUserAsync(currentUser.UserId, cancellationToken) is { } existing)
        {
            return existing;
        }

        string? displayName = null;
        try
        {
            displayName = (await identity.GetByIdAsync(currentUser.UserId, cancellationToken))?.DisplayName;
        }
        catch (IdentityUserLookupUnavailableException)
        {
            // The pledge must not fail because Identity is slow; the officer can correct the donor name later.
        }

        var donor = Donor.ForVolunteer(parishId, currentUser.UserId, displayName, DateTimeOffset.UtcNow);
        donors.Add(donor);
        return donor;
    }
}

/// <summary>
/// F-RES-13b <c>pledgeMaterialDonation</c> (VOLUNTEER of the parish): promise materials the work area actually requires.
/// Donation PLEDGED, nothing posted to stock (BR-39); <c>contribution.pledged</c> tells the officer.
/// </summary>
public sealed class PledgeMaterialCommandHandler(
    WorkAreaAccess access,
    IMaterialRequirementStore requirements,
    IMaterialCatalogStore catalog,
    VolunteerDonorResolver donorResolver,
    IDonationStore store,
    DonationProjector projector,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public async Task<DonationDto> HandleAsync(PledgeMaterialCommand command, CancellationToken cancellationToken = default)
    {
        var scope = await access.ForReadAsync(command.WorkAreaId, cancellationToken);
        WorkAreaAccess.EnsureWritable(scope);

        await MaterialRules.EnsureActiveAsync(catalog, command.Items.Select(item => item.MaterialId), cancellationToken);
        var required = (await requirements.ListByWorkAreaAsync(scope.WorkAreaId, cancellationToken))
            .Where(requirement => requirement.Status == RequirementStatus.Active)
            .Select(requirement => requirement.MaterialId)
            .ToHashSet();
        if (command.Items.FirstOrDefault(item => !required.Contains(item.MaterialId)) is { } notNeeded)
        {
            throw DomainRuleException.Validation(
                "PLEDGE_MATERIAL_NOT_REQUIRED",
                $"Material {notNeeded.MaterialId} is not required by this work area. Pledge only listed needs.");
        }

        var now = DateTimeOffset.UtcNow;
        var donor = await donorResolver.ResolveAsync(scope.ParishId, cancellationToken);
        var donation = Donation.PledgeMaterial(donor.DonorId, scope.SeasonId, scope.WorkAreaId, command.Items, command.Note, currentUser.UserId, now);
        store.Add(donation);
        await store.SaveChangesAsync(cancellationToken);

        await PledgeEvents.PublishAsync(publisher, donation, donor, scope.ParishId, scope.MaterialOfficerUserId, cancellationToken);
        return await projector.ToDtoAsync(donation, cancellationToken);
    }
}

/// <summary>F-RES-13b <c>pledgeMonetarySupport</c> (VOLUNTEER of the parish): promised money for a season (optionally a work area).</summary>
public sealed class PledgeMoneyCommandHandler(
    WorkAreaAccess access,
    IWorkAreaScopeStore scopes,
    VolunteerDonorResolver donorResolver,
    IDonationStore store,
    DonationProjector projector,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public async Task<DonationDto> HandleAsync(PledgeMoneyCommand command, CancellationToken cancellationToken = default)
    {
        WorkAreaScope target;
        if (command.WorkAreaId is { } workAreaId)
        {
            target = await access.ForReadAsync(workAreaId, cancellationToken);
            if (target.SeasonId != command.SeasonId)
            {
                throw DomainRuleException.Validation("DONATION_SEASON_MISMATCH", "The work area belongs to another season.");
            }
        }
        else
        {
            target = (await scopes.ListByParishAsync(access.RequireParish(), command.SeasonId, cancellationToken)).FirstOrDefault()
                ?? throw DomainRuleException.NotFound("SEASON_NOT_FOUND", "The season does not exist or is outside your parish.");
        }

        WorkAreaAccess.EnsureWritable(target);

        var donor = await donorResolver.ResolveAsync(target.ParishId, cancellationToken);
        var donation = Donation.PledgeMoney(donor.DonorId, command.SeasonId, command.WorkAreaId, command.Amount, command.Note,
            currentUser.UserId, DateTimeOffset.UtcNow);
        store.Add(donation);
        await store.SaveChangesAsync(cancellationToken);

        await PledgeEvents.PublishAsync(publisher, donation, donor, target.ParishId,
            command.WorkAreaId is null ? null : target.MaterialOfficerUserId, cancellationToken);
        return await projector.ToDtoAsync(donation, cancellationToken);
    }
}

/// <summary>
/// F-RES-13b <c>cancelDonationPledge</c> (MO of the receiving work area / season, or the VOLUNTEER who owns the pledge):
/// cancels what is still outstanding, with a reason; received receipts stay.
/// </summary>
public sealed class CancelPledgeCommandHandler(
    DonationOfficerScope officers,
    IDonationStore store,
    IDonorStore donors,
    DonationProjector projector,
    ICurrentUser currentUser)
{
    public async Task<DonationDto> HandleAsync(CancelPledgeCommand command, CancellationToken cancellationToken = default)
    {
        var donation = await store.GetAsync(command.DonationId, cancellationToken) ?? throw NotFound();

        if (currentUser.Role == GrottoWorksRoles.Volunteer)
        {
            if ((await donors.GetAsync(donation.DonorId, cancellationToken))?.UserId != currentUser.UserId)
            {
                throw NotFound();
            }
        }
        else
        {
            try
            {
                await officers.ResolveAsync(donation.Type, donation.WorkAreaId, donation.SeasonId, cancellationToken);
            }
            catch (DomainRuleException exception) when (exception.Kind == DomainErrorKind.NotFound)
            {
                throw NotFound();
            }
        }

        donation.EnsureVersion(command.Version);
        donation.CancelPledge(command.Reason, DateTimeOffset.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        return await projector.ToDtoAsync(donation, cancellationToken);
    }

    private static DomainRuleException NotFound() => DomainRuleException.NotFound("DONATION_NOT_FOUND", "The donation does not exist.");
}

internal static class PledgeEvents
{
    public static Task PublishAsync(
        IIntegrationEventPublisher publisher,
        Donation donation,
        Donor donor,
        Guid parishId,
        Guid? materialOfficerUserId,
        CancellationToken cancellationToken) =>
        publisher.PublishAsync(
            ContributionPledged.EventType,
            ContributionPledged.Version,
            new ContributionPledged(
                donation.DonationId, donor.DonorId, donor.UserId!.Value, parishId, donation.SeasonId, donation.WorkAreaId,
                materialOfficerUserId, donation.Type == DonationType.Money ? "MONEY" : "MATERIAL", donation.PledgedAmount, MoneyDto.Vnd,
                donation.Items.Select(item => new ContributionPledgeLine(item.DonationItemId, item.MaterialId, item.PledgedQuantity!.Value)).ToList(),
                donation.DonationDate),
            "donation",
            donation.DonationId,
            cancellationToken);
}
