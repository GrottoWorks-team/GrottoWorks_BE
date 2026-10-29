using Microsoft.EntityFrameworkCore;
using Resource.Application;
using Resource.Application.Contributions;
using Resource.Domain;
using Resource.Domain.Contributions;
using Resource.Infrastructure.Data;
using Resource.Infrastructure.Data.Configurations;

namespace Resource.Infrastructure.Contributions;

public sealed class DonorStore(ResourceDbContext dbContext) : IDonorStore
{
    public Task<Donor?> GetAsync(Guid donorId, CancellationToken cancellationToken = default) =>
        dbContext.Donors.FirstOrDefaultAsync(donor => donor.DonorId == donorId, cancellationToken);

    public Task<Donor?> FindByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Donors.FirstOrDefaultAsync(donor => donor.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Donor>> GetManyAsync(IReadOnlyCollection<Guid> donorIds, CancellationToken cancellationToken = default) =>
        await dbContext.Donors.AsNoTracking().Where(donor => donorIds.Contains(donor.DonorId)).ToListAsync(cancellationToken);

    public async Task<PagedResult<Donor>> SearchAsync(DonorSearch search, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Donors.AsNoTracking();
        if (search.ParishId is { } parishId)
        {
            query = query.Where(donor => donor.ParishId == parishId);
        }

        if (search.Linked is { } linked)
        {
            query = linked ? query.Where(donor => donor.UserId != null) : query.Where(donor => donor.UserId == null);
        }

        if (search.Search is { } text)
        {
            var pattern = "%" + LikePattern.Escape(text.Trim()) + "%";
            query = query.Where(donor =>
                EF.Functions.ILike(donor.DonorName, pattern, "\\") ||
                (donor.Phone != null && EF.Functions.ILike(donor.Phone, pattern, "\\")) ||
                (donor.Email != null && EF.Functions.ILike(donor.Email, pattern, "\\")));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var ordered = (search.SortField, search.SortDescending) switch
        {
            (DonorSortField.CreatedAt, false) => query.OrderBy(donor => donor.CreatedAt),
            (DonorSortField.CreatedAt, true) => query.OrderByDescending(donor => donor.CreatedAt),
            (_, false) => query.OrderBy(donor => donor.DonorName),
            (_, true) => query.OrderByDescending(donor => donor.DonorName)
        };
        var items = await ordered.ThenBy(donor => donor.DonorId)
            .Skip((search.Page - 1) * search.Size)
            .Take(search.Size)
            .ToListAsync(cancellationToken);
        return new PagedResult<Donor>(items, total);
    }

    public void Add(Donor donor) => dbContext.Donors.Add(donor);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(DonorConfiguration.UserIndex))
        {
            throw new DomainRuleException("DONOR_USER_ALREADY_LINKED", "This user account is already linked to a donor.");
        }
        catch (DbUpdateConcurrencyException exception) when (exception.Entries.Any(entry => entry.Entity is Donor))
        {
            throw new DomainRuleException(
                "DONOR_VERSION_MISMATCH",
                "The donor was changed by someone else. Reload it and try again.",
                DomainErrorKind.PreconditionFailed);
        }
    }
}
