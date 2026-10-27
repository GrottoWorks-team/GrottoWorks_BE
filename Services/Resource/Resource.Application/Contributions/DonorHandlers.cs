using BuildingBlocks.Security;
using Resource.Application.Scope;
using Resource.Application.Users;
using Resource.Domain;
using Resource.Domain.Contributions;

namespace Resource.Application.Contributions;

/// <summary><c>Donor</c> (API Contract v1.3.2).</summary>
public sealed record DonorDto(
    Guid Id,
    Guid ParishId,
    Guid? UserId,
    string DonorName,
    string? Phone,
    string? Email,
    string? Address,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    long Version)
{
    public static DonorDto From(Donor donor) =>
        new(donor.DonorId, donor.ParishId, donor.UserId, donor.DonorName, donor.Phone, donor.Email, donor.Address, donor.Note,
            donor.CreatedAt, donor.UpdatedAt, donor.Version);
}

public enum DonorSortField
{
    Name,
    CreatedAt
}

public sealed record DonorSearch(
    Guid? ParishId,
    string? Search,
    bool? Linked,
    DonorSortField SortField,
    bool SortDescending,
    int Page,
    int Size);

public sealed record DonorFilter(int Page, int Size, Guid? ParishId, string? Search, bool? Linked, DonorSortField SortField, bool SortDescending);

public sealed record CreateDonorCommand(
    Guid? ParishId,
    Guid? UserId,
    string? DonorName,
    string? Phone,
    string? Email,
    string? Address,
    string? Note);

public sealed record UpdateDonorCommand(
    Guid DonorId,
    long? Version,
    string? DonorName,
    string? Phone,
    string? Email,
    string? Address,
    string? Note);

public interface IDonorStore
{
    /// <summary>Tracked.</summary>
    Task<Donor?> GetAsync(Guid donorId, CancellationToken cancellationToken = default);

    Task<Donor?> FindByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Donor>> GetManyAsync(IReadOnlyCollection<Guid> donorIds, CancellationToken cancellationToken = default);

    Task<PagedResult<Donor>> SearchAsync(DonorSearch search, CancellationToken cancellationToken = default);

    void Add(Donor donor);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Who may see or change a donor: MO of the donor's parish, or ADMIN (any parish). Other parishes see 404 so
/// donor contact details never leak across tenants.
/// </summary>
public sealed class DonorAccess(IDonorStore store, ICurrentUser currentUser)
{
    public bool IsAdmin => currentUser.Role == GrottoWorksRoles.Admin;

    public async Task<Donor> GetVisibleAsync(Guid donorId, CancellationToken cancellationToken)
    {
        var donor = await store.GetAsync(donorId, cancellationToken);
        if (donor is null || !IsVisible(donor))
        {
            throw DomainRuleException.NotFound("DONOR_NOT_FOUND", "The donor does not exist.");
        }

        return donor;
    }

    public bool IsVisible(Donor donor) => IsAdmin || donor.ParishId == currentUser.ParishId;

    /// <summary>MO: always the own parish (a different <paramref name="requested"/> is refused). ADMIN: must name one.</summary>
    public Guid ParishForWrite(Guid? requested)
    {
        if (IsAdmin)
        {
            return requested
                ?? throw DomainRuleException.Validation("DONOR_PARISH_REQUIRED", "An administrator must choose the parish of the donor.");
        }

        var own = currentUser.ParishId
            ?? throw DomainRuleException.Forbidden("PARISH_SCOPE_REQUIRED", "Your account is not attached to a parish.");
        if (requested is { } other && other != own)
        {
            throw DomainRuleException.Forbidden("PARISH_SCOPE_DENIED", "Donors can only be recorded for your own parish.");
        }

        return own;
    }

    /// <summary>List scope: MO → own parish; ADMIN → the requested parish or all.</summary>
    public Guid? ParishForRead(Guid? requested) =>
        IsAdmin
            ? requested
            : ParishForWrite(requested);
}

/// <summary>F-RES-12 <c>listDonors</c> (MO, ADMIN): search by name, phone or email; no automatic merge.</summary>
public sealed class ListDonorsQueryHandler(DonorAccess access, IDonorStore store)
{
    public async Task<PagedResult<DonorDto>> HandleAsync(DonorFilter filter, CancellationToken cancellationToken = default)
    {
        var page = await store.SearchAsync(
            new DonorSearch(access.ParishForRead(filter.ParishId), filter.Search, filter.Linked, filter.SortField, filter.SortDescending,
                filter.Page, filter.Size),
            cancellationToken);
        return new PagedResult<DonorDto>(page.Items.Select(DonorDto.From).ToList(), page.TotalItems);
    }
}

/// <summary>
/// F-RES-12 <c>createDonor</c> (MO, ADMIN). An optional <c>userId</c> links a volunteer account: it must exist in
/// Identity, belong to the same parish and not be linked to another donor yet (<c>donor.user_id</c> is unique).
/// </summary>
public sealed class CreateDonorCommandHandler(
    DonorAccess access,
    IDonorStore store,
    IIdentityUserLookup identity,
    ICurrentUser currentUser)
{
    public async Task<DonorDto> HandleAsync(CreateDonorCommand command, CancellationToken cancellationToken = default)
    {
        var parishId = access.ParishForWrite(command.ParishId);
        var donor = Donor.Create(parishId, command.UserId, command.DonorName, command.Phone, command.Email, command.Address,
            command.Note, currentUser.UserId, DateTimeOffset.UtcNow);

        if (command.UserId is { } userId)
        {
            await DonorUserLink.EnsureLinkableAsync(identity, store, userId, parishId, cancellationToken);
        }

        store.Add(donor);
        await store.SaveChangesAsync(cancellationToken);
        return DonorDto.From(donor);
    }
}

/// <summary>F-RES-12 <c>updateDonor</c> (MO, ADMIN): name and contact details; the account link is fixed at creation.</summary>
public sealed class UpdateDonorCommandHandler(DonorAccess access, IDonorStore store)
{
    public async Task<DonorDto> HandleAsync(UpdateDonorCommand command, CancellationToken cancellationToken = default)
    {
        var donor = await access.GetVisibleAsync(command.DonorId, cancellationToken);
        donor.EnsureVersion(command.Version);
        donor.Update(command.DonorName, command.Phone, command.Email, command.Address, command.Note, DateTimeOffset.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        return DonorDto.From(donor);
    }
}

internal static class DonorUserLink
{
    public static async Task EnsureLinkableAsync(
        IIdentityUserLookup identity,
        IDonorStore store,
        Guid userId,
        Guid parishId,
        CancellationToken cancellationToken)
    {
        IdentityUser? user;
        try
        {
            user = await identity.GetByIdAsync(userId, cancellationToken);
        }
        catch (IdentityUserLookupUnavailableException)
        {
            throw new DomainRuleException(
                "IDENTITY_UNAVAILABLE",
                "The linked account could not be checked because Identity is unavailable. Try again later.",
                DomainErrorKind.ServiceUnavailable);
        }

        if (user is null)
        {
            throw DomainRuleException.Validation("DONOR_USER_NOT_FOUND", "The linked user account does not exist.");
        }

        if (user.ParishId != parishId)
        {
            throw DomainRuleException.Validation("DONOR_USER_OTHER_PARISH", "The linked user account belongs to another parish.");
        }

        if (await store.FindByUserAsync(userId, cancellationToken) is not null)
        {
            throw new DomainRuleException("DONOR_USER_ALREADY_LINKED", "This user account is already linked to a donor.");
        }
    }
}
