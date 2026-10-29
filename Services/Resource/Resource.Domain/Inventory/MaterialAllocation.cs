namespace Resource.Domain.Inventory;

public sealed record AllocationLineInput(Guid MaterialId, decimal Quantity);

/// <summary>
/// <c>material_allocation</c> (ERD v3.6.2, F-RES-15): confirmed stock of a work area handed out, optionally to a task.
/// Every allocation counts as allocated (API Contract 7.1: unallocated = confirmedAvailable − allocated); usable
/// remainders come back later through verified task returns (BR-49). <c>allocated_quantity</c> is history and is never
/// rewritten (BR-34, BR-51).
/// </summary>
public sealed class MaterialAllocation
{
    public const int MaxItems = 50;
    public const int MaxNoteLength = 2000;

    private readonly List<MaterialAllocationItem> _items = [];

    private MaterialAllocation()
    {
    }

    public Guid MaterialAllocationId { get; private set; }

    public Guid WorkAreaId { get; private set; }

    public Guid? TaskId { get; private set; }

    public Guid AllocatedByUserId { get; private set; }

    public DateTimeOffset AllocationDate { get; private set; }

    public string? Note { get; private set; }

    public IReadOnlyList<MaterialAllocationItem> Items => _items;

    public static MaterialAllocation Create(
        Guid workAreaId,
        Guid? taskId,
        IReadOnlyCollection<AllocationLineInput> lines,
        DateTimeOffset allocationDate,
        string? note,
        Guid allocatedByUserId,
        DateTimeOffset now)
    {
        if (allocationDate > now + TimeSpan.FromMinutes(5))
        {
            throw DomainRuleException.Validation("ALLOCATION_DATE_IN_FUTURE", "An allocation cannot be dated in the future.");
        }

        if (lines.Count == 0)
        {
            throw DomainRuleException.Validation("ALLOCATION_ITEMS_REQUIRED", "Allocate at least one material.");
        }

        if (lines.Count > MaxItems)
        {
            throw DomainRuleException.Validation("ALLOCATION_TOO_MANY_ITEMS", $"An allocation holds at most {MaxItems} materials.");
        }

        if (lines.GroupBy(line => line.MaterialId).Any(group => group.Count() > 1))
        {
            throw DomainRuleException.Validation("ALLOCATION_DUPLICATE_MATERIAL", "Each material can appear only once in an allocation.");
        }

        var cleaned = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (cleaned is { Length: > MaxNoteLength })
        {
            throw DomainRuleException.Validation("ALLOCATION_NOTE_TOO_LONG", $"Note must not exceed {MaxNoteLength} characters.");
        }

        var allocation = new MaterialAllocation
        {
            MaterialAllocationId = Guid.NewGuid(),
            WorkAreaId = workAreaId,
            TaskId = taskId,
            AllocatedByUserId = allocatedByUserId,
            AllocationDate = allocationDate.ToUniversalTime(),
            Note = cleaned
        };
        allocation._items.AddRange(lines.Select(line => new MaterialAllocationItem(allocation.MaterialAllocationId, line)));
        return allocation;
    }
}

/// <summary><c>material_allocation_item</c> (ERD v3.6.2): the quantity actually handed out (history, BR-34).</summary>
public sealed class MaterialAllocationItem
{
    private MaterialAllocationItem()
    {
    }

    internal MaterialAllocationItem(Guid allocationId, AllocationLineInput line)
    {
        MaterialAllocationItemId = Guid.NewGuid();
        MaterialAllocationId = allocationId;
        MaterialId = line.MaterialId;
        AllocatedQuantity = Quantity.Positive(line.Quantity, "ALLOCATION_QUANTITY_INVALID", "Allocated quantity");
    }

    public Guid MaterialAllocationItemId { get; private set; }

    public Guid MaterialAllocationId { get; private set; }

    public Guid MaterialId { get; private set; }

    public decimal AllocatedQuantity { get; private set; }
}
