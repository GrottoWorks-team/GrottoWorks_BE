namespace Resource.Domain.Scope;

public sealed record TaskMaterialPlanInput(Guid MaterialId, decimal RequiredQuantity, string? Note);

// Read-only replica of TaskExecution requirements, never an inventory ledger or a manual area requirement.
public sealed class TaskMaterialPlan
{
    private readonly List<TaskMaterialPlanItem> _items = [];
    private TaskMaterialPlan() { }
    public Guid TaskId { get; private set; }
    public Guid WorkAreaId { get; private set; }
    public Guid ParishId { get; private set; }
    public Guid SeasonId { get; private set; }
    public long SourceVersion { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<TaskMaterialPlanItem> Items => _items;

    public static TaskMaterialPlan Create(Guid taskId, Guid areaId, Guid parishId, Guid seasonId,
        long version, IReadOnlyList<TaskMaterialPlanInput> items, DateTimeOffset now)
    {
        if (taskId == Guid.Empty || areaId == Guid.Empty || parishId == Guid.Empty || seasonId == Guid.Empty)
            throw new FormatException("Task material snapshot ownership is incomplete.");
        var plan = new TaskMaterialPlan { TaskId = taskId, WorkAreaId = areaId, ParishId = parishId, SeasonId = seasonId };
        plan.Apply(areaId, parishId, seasonId, version, items, now);
        return plan;
    }

    public bool Apply(Guid areaId, Guid parishId, Guid seasonId, long version,
        IReadOnlyList<TaskMaterialPlanInput> items, DateTimeOffset now)
    {
        if (areaId != WorkAreaId || parishId != ParishId || seasonId != SeasonId || version < 1)
            throw new FormatException("Task material snapshot changed immutable ownership or has an invalid version.");
        if (items.Count > 100 || items.Any(item => item is null || item.MaterialId == Guid.Empty ||
            item.RequiredQuantity <= 0 || item.RequiredQuantity > 9999999999.99m ||
            decimal.Round(item.RequiredQuantity, 2) != item.RequiredQuantity || item.Note?.Length > 2000) ||
            items.Select(item => item.MaterialId).Distinct().Count() != items.Count)
            throw new FormatException("Task material snapshot contains invalid or duplicate requirements.");
        if (version <= SourceVersion) return false;

        var existing = _items.ToDictionary(item => item.MaterialId);
        var next = new List<TaskMaterialPlanItem>(items.Count);
        foreach (var input in items)
        {
            if (existing.TryGetValue(input.MaterialId, out var current))
            {
                current.Apply(input);
                next.Add(current);
            }
            else next.Add(TaskMaterialPlanItem.Create(TaskId, input));
        }
        _items.Clear();
        _items.AddRange(next);
        SourceVersion = version;
        UpdatedAt = now.ToUniversalTime();
        return true;
    }
}

public sealed class TaskMaterialPlanItem
{
    private TaskMaterialPlanItem() { }
    public Guid TaskId { get; private set; }
    public Guid MaterialId { get; private set; }
    public decimal RequiredQuantity { get; private set; }
    public string? Note { get; private set; }
    internal static TaskMaterialPlanItem Create(Guid taskId, TaskMaterialPlanInput input)
    {
        var row = new TaskMaterialPlanItem { TaskId = taskId, MaterialId = input.MaterialId };
        row.Apply(input);
        return row;
    }
    internal void Apply(TaskMaterialPlanInput input)
    {
        RequiredQuantity = input.RequiredQuantity;
        Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
    }
}
