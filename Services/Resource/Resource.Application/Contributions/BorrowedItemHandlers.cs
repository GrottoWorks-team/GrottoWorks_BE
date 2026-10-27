using BuildingBlocks.Security;
using Resource.Application.Abstractions;
using Resource.Application.Scope;
using Resource.Contracts.Events;
using Resource.Domain;
using Resource.Domain.Contributions;
using Resource.Domain.Scope;

namespace Resource.Application.Contributions;

public sealed record BorrowTransactionDto(
    Guid Id,
    DateOnly BorrowDate,
    DateOnly? ReturnDate,
    ReturnCondition? Condition,
    decimal? Quantity,
    string Kind,
    Guid? RecordedBy,
    string? Note)
{
    public static BorrowTransactionDto From(BorrowTransaction transaction) =>
        new(transaction.BorrowTransactionId, transaction.BorrowDate, transaction.ReturnDate, transaction.ReturnCondition,
            transaction.ReturnedQuantity, transaction.IsIssue ? (transaction.ReturnDate is null ? "ISSUE" : "RETURN") : "INCIDENT",
            transaction.RecordedByUserId, transaction.Note);
}

/// <summary><c>BorrowedItem</c> / <c>LoanOffer</c> (API Contract v1.3.2): one table, the offer is the item in OFFERED.</summary>
public sealed record BorrowedItemDto(
    Guid Id,
    Guid SeasonId,
    Guid? WorkAreaId,
    Guid? TaskId,
    Guid OfferedBy,
    Guid? AcceptedBy,
    string OwnerName,
    string? OwnerPhone,
    string? OwnerAddress,
    string? OwnerNote,
    string ItemName,
    decimal Quantity,
    string? Description,
    string? InitialCondition,
    DateOnly ExpectedReturnDate,
    BorrowedItemStatus Status,
    bool Overdue,
    string? RejectionReason,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<BorrowTransactionDto> Transactions,
    long Version)
{
    public static BorrowedItemDto From(BorrowedItem item, DateOnly today) =>
        new(item.BorrowedItemId, item.SeasonId, item.WorkAreaId, item.TaskId, item.OfferedByUserId, item.AcceptedByUserId, item.OwnerName,
            item.OwnerPhone, item.OwnerAddress, item.OwnerNote, item.ItemName, item.Quantity, item.Description, item.InitialCondition,
            item.ExpectedReturnDate, item.Status, item.IsOverdue(today), item.RejectionReason, item.AcceptedAt, item.CreatedAt,
            item.Transactions.OrderBy(t => t.BorrowDate).ThenBy(t => t.ReturnDate is null ? 0 : 1).Select(BorrowTransactionDto.From).ToList(),
            item.Version);
}

public sealed record BorrowedItemSearch(
    IReadOnlyCollection<Guid> SeasonIds,
    Guid? WorkAreaId,
    IReadOnlyCollection<BorrowedItemStatus> Statuses,
    DateOnly? DueOnOrBefore,
    int Page,
    int Size);

public sealed record BorrowedItemFilter(int Page, int Size, Guid? SeasonId, Guid? WorkAreaId, BorrowedItemStatus? Status, bool? Overdue);

public sealed record OfferItemForLoanCommand(LoanOfferInput Offer);

public sealed record AcceptLoanOfferCommand(Guid ItemId, long? Version, Guid? WorkAreaId, DateOnly? ExpectedReturnDate);

public sealed record BorrowedItemCommand(Guid ItemId, long? Version);

public sealed record RejectLoanOfferCommand(Guid ItemId, long? Version, string? Reason);

public sealed record IssueBorrowedItemCommand(Guid ItemId, long? Version, DateOnly? BorrowDate, string? Note);

public sealed record AssignBorrowedItemCommand(Guid ItemId, long? Version, Guid WorkAreaId, Guid? TaskId);

public sealed record ReturnBorrowedItemCommand(Guid ItemId, long? Version, DateOnly? ReturnDate, ReturnCondition Condition, decimal? ReturnedQuantity, string? Note);

public sealed record BorrowedItemIncidentCommand(Guid ItemId, ReturnCondition Condition, decimal? Quantity, DateOnly? Date, string? Description);

public interface IBorrowedItemStore
{
    /// <summary>Tracked, with transactions.</summary>
    Task<BorrowedItem?> GetAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Earliest expected return first; transactions included.</summary>
    Task<PagedResult<BorrowedItem>> SearchAsync(BorrowedItemSearch search, CancellationToken cancellationToken = default);

    /// <summary>Tracked: items out (ISSUED / IN_USE) due on or before the date and not reminded today.</summary>
    Task<IReadOnlyList<BorrowedItem>> ListToRemindAsync(DateOnly dueOnOrBefore, DateOnly today, CancellationToken cancellationToken = default);

    void Add(BorrowedItem item);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>The parish calendar date (Vietnam, UTC+7) — return dates are local dates.</summary>
public static class ParishClock
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(Offset).DateTime);
}

/// <summary>Loads a borrowed item the caller may act on as material officer (work area, or season when unassigned).</summary>
public sealed class BorrowedItemAccess(IBorrowedItemStore store, MaterialOfficerScope officers, WorkAreaAccess access, IWorkAreaScopeStore scopes)
{
    public async Task<BorrowedItem> ForOfficerAsync(Guid itemId, Guid? targetWorkAreaId, CancellationToken cancellationToken)
    {
        var item = await store.GetAsync(itemId, cancellationToken) ?? throw NotFound();
        try
        {
            await officers.ForWorkAreaOrSeasonAsync(targetWorkAreaId ?? item.WorkAreaId, item.SeasonId, "LOAN_SEASON_MISMATCH", "LOAN_SEASON_REQUIRED",
                cancellationToken);
        }
        catch (DomainRuleException exception) when (exception.Kind == DomainErrorKind.NotFound)
        {
            throw NotFound();
        }

        return item;
    }

    /// <summary>Season ids of the caller's parish (optionally one season).</summary>
    public async Task<IReadOnlyList<Guid>> ParishSeasonsAsync(Guid? seasonId, CancellationToken cancellationToken) =>
        (await scopes.ListByParishAsync(access.RequireParish(), seasonId, cancellationToken)).Select(area => area.SeasonId).Distinct().ToList();

    public static DomainRuleException NotFound() => DomainRuleException.NotFound("BORROWED_ITEM_NOT_FOUND", "The borrowed item does not exist.");
}

/// <summary>
/// F-RES-18 <c>offerItemForLoan</c> (VOLUNTEER of the parish): enter equipment someone lends, with the lender's name and
/// contact (BR-41, D-05) — the volunteer stays the contact point. <c>loan.offer.submitted</c> tells the officers.
/// </summary>
public sealed class OfferItemForLoanCommandHandler(
    WorkAreaAccess access,
    IWorkAreaScopeStore scopes,
    IBorrowedItemStore store,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser)
{
    public async Task<BorrowedItemDto> HandleAsync(OfferItemForLoanCommand command, CancellationToken cancellationToken = default)
    {
        var offer = command.Offer;
        var areas = await scopes.ListByParishAsync(access.RequireParish(), offer.SeasonId, cancellationToken);
        if (areas.Count == 0)
        {
            throw DomainRuleException.NotFound("SEASON_NOT_FOUND", "The season does not exist or is outside your parish.");
        }

        if (offer.WorkAreaId is { } workAreaId && areas.All(area => area.WorkAreaId != workAreaId))
        {
            throw DomainRuleException.Validation("LOAN_SEASON_MISMATCH", "The work area does not belong to this season.");
        }

        WorkAreaAccess.EnsureWritable(areas[0]);
        var now = DateTimeOffset.UtcNow;
        var today = ParishClock.Today(now);
        var item = BorrowedItem.Offer(offer, currentUser.UserId, today, now);
        store.Add(item);
        await store.SaveChangesAsync(cancellationToken);

        var officers = areas.Where(area => offer.WorkAreaId is null || area.WorkAreaId == offer.WorkAreaId)
            .Select(area => area.MaterialOfficerUserId).OfType<Guid>().Distinct().ToList();
        await publisher.PublishAsync(
            LoanOfferSubmitted.EventType,
            LoanOfferSubmitted.Version,
            new LoanOfferSubmitted(item.BorrowedItemId, areas[0].ParishId, item.SeasonId, item.WorkAreaId, officers, item.OfferedByUserId,
                item.ItemName, item.Quantity, item.OwnerName, item.ExpectedReturnDate, now),
            "borrowed_item",
            item.BorrowedItemId,
            cancellationToken);
        return BorrowedItemDto.From(item, today);
    }
}

/// <summary>F-RES-18 <c>listLoanOffers</c> (MO): offers of the parish, OFFERED by default.</summary>
public sealed class ListLoanOffersQueryHandler(BorrowedItemAccess access, IBorrowedItemStore store)
{
    public async Task<PagedResult<BorrowedItemDto>> HandleAsync(int page, int size, BorrowedItemStatus? status, CancellationToken cancellationToken = default)
    {
        if (status is { } requested && requested is not (BorrowedItemStatus.Offered or BorrowedItemStatus.Rejected or BorrowedItemStatus.Accepted))
        {
            throw new DomainRuleException("INVALID_FILTER_VALUE", "'status' of a loan offer is OFFERED, ACCEPTED or REJECTED.", DomainErrorKind.BadRequest);
        }

        var seasons = await access.ParishSeasonsAsync(null, cancellationToken);
        var result = await store.SearchAsync(new BorrowedItemSearch(seasons, null, [status ?? BorrowedItemStatus.Offered], null, page, size), cancellationToken);
        var today = ParishClock.Today(DateTimeOffset.UtcNow);
        return new PagedResult<BorrowedItemDto>(result.Items.Select(item => BorrowedItemDto.From(item, today)).ToList(), result.TotalItems);
    }
}

/// <summary>F-RES-18 <c>listBorrowedItems</c> (PARISH, LEADER, MO): accepted items onwards; <c>overdue=true</c> narrows to late ones.</summary>
public sealed class ListBorrowedItemsQueryHandler(BorrowedItemAccess access, IBorrowedItemStore store)
{
    private static readonly BorrowedItemStatus[] Borrowed =
        [BorrowedItemStatus.Accepted, BorrowedItemStatus.Issued, BorrowedItemStatus.InUse, BorrowedItemStatus.Returned];

    private static readonly BorrowedItemStatus[] Out = [BorrowedItemStatus.Issued, BorrowedItemStatus.InUse];

    public async Task<PagedResult<BorrowedItemDto>> HandleAsync(BorrowedItemFilter filter, CancellationToken cancellationToken = default)
    {
        var seasons = await access.ParishSeasonsAsync(filter.SeasonId, cancellationToken);
        if (seasons.Count == 0)
        {
            return new PagedResult<BorrowedItemDto>([], 0);
        }

        var today = ParishClock.Today(DateTimeOffset.UtcNow);
        var statuses = filter.Overdue == true ? Out : filter.Status is { } status ? [status] : Borrowed;
        var result = await store.SearchAsync(
            new BorrowedItemSearch(seasons, filter.WorkAreaId, statuses, filter.Overdue == true ? today.AddDays(-1) : null, filter.Page, filter.Size),
            cancellationToken);
        return new PagedResult<BorrowedItemDto>(result.Items.Select(item => BorrowedItemDto.From(item, today)).ToList(), result.TotalItems);
    }
}

/// <summary>F-RES-18 <c>listOverdueBorrowedItems</c> (MO): items still out and due within <c>withinDays</c> of the date (default today + 2).</summary>
public sealed class ListOverdueBorrowedItemsQueryHandler(BorrowedItemAccess access, IBorrowedItemStore store)
{
    public async Task<IReadOnlyList<BorrowedItemDto>> HandleAsync(DateOnly? date, int withinDays, CancellationToken cancellationToken = default)
    {
        var seasons = await access.ParishSeasonsAsync(null, cancellationToken);
        if (seasons.Count == 0)
        {
            return [];
        }

        var today = ParishClock.Today(DateTimeOffset.UtcNow);
        var reference = date ?? today;
        var result = await store.SearchAsync(
            new BorrowedItemSearch(seasons, null, [BorrowedItemStatus.Issued, BorrowedItemStatus.InUse], reference.AddDays(withinDays), 1, 500),
            cancellationToken);
        return result.Items.Select(item => BorrowedItemDto.From(item, reference)).ToList();
    }
}

/// <summary>F-RES-18 officer commands on one item: accept, reject, issue, assign, return, incident.</summary>
public sealed class BorrowedItemCommandHandler(
    BorrowedItemAccess access,
    IBorrowedItemStore store,
    ITaskProjectionStore tasks,
    ICurrentUser currentUser)
{
    public async Task<BorrowedItemDto> AcceptAsync(AcceptLoanOfferCommand command, CancellationToken cancellationToken = default)
    {
        var item = await access.ForOfficerAsync(command.ItemId, command.WorkAreaId, cancellationToken);
        item.EnsureVersion(command.Version);
        var now = DateTimeOffset.UtcNow;
        item.Accept(currentUser.UserId, command.WorkAreaId, command.ExpectedReturnDate, ParishClock.Today(now), now);
        return await SaveAsync(item, cancellationToken);
    }

    public async Task<BorrowedItemDto> RejectAsync(RejectLoanOfferCommand command, CancellationToken cancellationToken = default)
    {
        var item = await access.ForOfficerAsync(command.ItemId, null, cancellationToken);
        item.EnsureVersion(command.Version);
        item.Reject(command.Reason);
        return await SaveAsync(item, cancellationToken);
    }

    public async Task<BorrowedItemDto> IssueAsync(IssueBorrowedItemCommand command, CancellationToken cancellationToken = default)
    {
        var item = await access.ForOfficerAsync(command.ItemId, null, cancellationToken);
        item.EnsureVersion(command.Version);
        var today = ParishClock.Today(DateTimeOffset.UtcNow);
        item.Issue(command.BorrowDate ?? today, command.Note, currentUser.UserId, today);
        return await SaveAsync(item, cancellationToken);
    }

    /// <summary>The officer of the target work area assigns it (same season); a task must belong to that work area.</summary>
    public async Task<BorrowedItemDto> AssignAsync(AssignBorrowedItemCommand command, CancellationToken cancellationToken = default)
    {
        var item = await access.ForOfficerAsync(command.ItemId, command.WorkAreaId, cancellationToken);
        item.EnsureVersion(command.Version);
        if (command.TaskId is { } taskId)
        {
            var task = await tasks.GetAsync(taskId, cancellationToken);
            if (task is null || task.WorkAreaId != command.WorkAreaId || task.IsClosed)
            {
                throw DomainRuleException.Validation("TASK_UNKNOWN", "The task does not exist or is closed in this work area.");
            }
        }

        item.Assign(command.WorkAreaId, command.TaskId);
        return await SaveAsync(item, cancellationToken);
    }

    public async Task<BorrowedItemDto> ReturnAsync(ReturnBorrowedItemCommand command, CancellationToken cancellationToken = default)
    {
        var item = await access.ForOfficerAsync(command.ItemId, null, cancellationToken);
        item.EnsureVersion(command.Version);
        var today = ParishClock.Today(DateTimeOffset.UtcNow);
        item.Return(command.ReturnDate ?? today, command.Condition, command.ReturnedQuantity, command.Note, currentUser.UserId, today);
        return await SaveAsync(item, cancellationToken);
    }

    public async Task<BorrowTransactionDto> RecordIncidentAsync(BorrowedItemIncidentCommand command, CancellationToken cancellationToken = default)
    {
        var item = await access.ForOfficerAsync(command.ItemId, null, cancellationToken);
        var today = ParishClock.Today(DateTimeOffset.UtcNow);
        var incident = item.RecordIncident(command.Condition, command.Quantity, command.Date ?? today, command.Description, currentUser.UserId, today);
        await store.SaveChangesAsync(cancellationToken);
        return BorrowTransactionDto.From(incident);
    }

    private async Task<BorrowedItemDto> SaveAsync(BorrowedItem item, CancellationToken cancellationToken)
    {
        await store.SaveChangesAsync(cancellationToken);
        return BorrowedItemDto.From(item, ParishClock.Today(DateTimeOffset.UtcNow));
    }
}

/// <summary>
/// F-RES-18 reminder job (BA 8.8 "nhắc trước và sau ngày trả"): once a day per item still out — due today or tomorrow →
/// <c>borrowed-item.return-due</c>; past due → <c>borrowed-item.overdue</c>. <c>last_reminded_on</c> keeps it to one
/// reminder per item per day, whichever instance runs the job.
/// </summary>
public sealed class BorrowedItemReminderJob(IBorrowedItemStore store, IWorkAreaScopeStore scopes, IIntegrationEventPublisher publisher)
{
    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var today = ParishClock.Today(now);
        var items = await store.ListToRemindAsync(today.AddDays(1), today, cancellationToken);
        foreach (var item in items)
        {
            var areas = await scopes.ListBySeasonAsync(item.SeasonId, cancellationToken);
            var officers = areas.Where(area => item.WorkAreaId is null || area.WorkAreaId == item.WorkAreaId)
                .Select(area => area.MaterialOfficerUserId).OfType<Guid>().Distinct().ToList();
            var overdueDays = today.DayNumber - item.ExpectedReturnDate.DayNumber;
            await publisher.PublishAsync(
                overdueDays > 0 ? BorrowedItemReminder.OverdueEventType : BorrowedItemReminder.ReturnDueEventType,
                BorrowedItemReminder.Version,
                new BorrowedItemReminder(item.BorrowedItemId, areas.FirstOrDefault()?.ParishId ?? Guid.Empty, item.SeasonId, item.WorkAreaId,
                    item.TaskId, officers, item.OfferedByUserId, item.ItemName, item.OwnerName, item.OwnerPhone, item.ExpectedReturnDate,
                    Math.Max(0, overdueDays)),
                "borrowed_item",
                item.BorrowedItemId,
                cancellationToken);
            item.MarkReminded(today);
        }

        await store.SaveChangesAsync(cancellationToken);
        return items.Count;
    }
}
