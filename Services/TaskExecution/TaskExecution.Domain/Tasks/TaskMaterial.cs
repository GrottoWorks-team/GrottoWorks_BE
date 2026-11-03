namespace TaskExecution.Domain.Tasks;

public sealed record TaskMaterialRequirement(Guid MaterialId, decimal RequiredQuantity, string? Note);

public sealed class TaskMaterial
{
    private TaskMaterial() { }
    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid MaterialId { get; private set; }
    public decimal RequiredQuantity { get; private set; }
    public string? Note { get; private set; }
    internal static TaskMaterial Create(Guid taskId, TaskMaterialRequirement requirement) => new()
    {
        Id = Guid.NewGuid(), TaskId = taskId, MaterialId = requirement.MaterialId,
        RequiredQuantity = requirement.RequiredQuantity, Note = requirement.Note
    };
    internal void Update(TaskMaterialRequirement requirement)
    {
        RequiredQuantity = requirement.RequiredQuantity;
        Note = requirement.Note;
    }
}

public sealed partial class WorkTask
{
    private readonly List<TaskMaterial> _materials = [];
    public IReadOnlyCollection<TaskMaterial> Materials => _materials;

    public static IReadOnlyList<TaskMaterialRequirement> NormalizeMaterials(IReadOnlyList<TaskMaterialRequirement> requirements)
    {
        if (requirements.Count > 100)
            throw DomainRuleException.Validation("TASK_MATERIAL_LIMIT_EXCEEDED", "At most 100 material requirements are allowed.");
        var normalized = new List<TaskMaterialRequirement>(requirements.Count);
        var ids = new HashSet<Guid>();
        foreach (var requirement in requirements)
        {
            if (requirement is null)
                throw DomainRuleException.Validation("TASK_MATERIAL_INVALID", "Material entries must not be null.");
            if (requirement.MaterialId == Guid.Empty)
                throw DomainRuleException.Validation("TASK_MATERIAL_ID_INVALID", "Material ID is required.");
            if (!ids.Add(requirement.MaterialId))
                throw DomainRuleException.Validation("TASK_MATERIAL_DUPLICATE", "A material can appear only once per task.");
            if (requirement.RequiredQuantity <= 0 || requirement.RequiredQuantity > 9999999999.99m ||
                decimal.Round(requirement.RequiredQuantity, 2) != requirement.RequiredQuantity)
                throw DomainRuleException.Validation("TASK_MATERIAL_QUANTITY_INVALID", "Quantity must be positive, fit decimal(12,2), and have at most two decimal places.");
            var note = string.IsNullOrWhiteSpace(requirement.Note) ? null : requirement.Note.Trim();
            if (note?.Length > 2000)
                throw DomainRuleException.Validation("TASK_MATERIAL_NOTE_INVALID", "Material note must not exceed 2000 characters.");
            normalized.Add(requirement with { Note = note });
        }
        return normalized.OrderBy(requirement => requirement.MaterialId).ToArray();
    }

    public bool ReplaceMaterials(IReadOnlyList<TaskMaterialRequirement> requirements, int? expectedVersion, DateTimeOffset now)
    {
        EnsurePlanningCanBeUpdated(expectedVersion);
        if (Type == TaskType.Purchase)
            throw new DomainRuleException("TASK_TYPE_NOT_SUPPORTED", "Purchase task items are owned by its purchase request.");
        var normalized = NormalizeMaterials(requirements);
        var current = _materials.OrderBy(row => row.MaterialId).ToArray();
        if (current.Length == normalized.Count && current.Select(row => new TaskMaterialRequirement(row.MaterialId, row.RequiredQuantity, row.Note)).SequenceEqual(normalized))
            return false;
        var byId = current.ToDictionary(row => row.MaterialId);
        var next = new List<TaskMaterial>(normalized.Count);
        foreach (var requirement in normalized)
        {
            if (byId.TryGetValue(requirement.MaterialId, out var existing))
            {
                existing.Update(requirement);
                next.Add(existing);
            }
            else next.Add(TaskMaterial.Create(Id, requirement));
        }
        _materials.Clear();
        _materials.AddRange(next);
        UpdatedAt = now.ToUniversalTime();
        Version++;
        return true;
    }
}
