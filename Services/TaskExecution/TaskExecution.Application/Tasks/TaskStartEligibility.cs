using TaskExecution.Application.Assignments;
using TaskExecution.Application.Scopes;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;
using TaskExecution.Domain.Tasks;
using TaskState = TaskExecution.Domain.Tasks.TaskStatus;

namespace TaskExecution.Application.Tasks;

/// <summary>Vật tư còn thiếu so với kế hoạch của nhiệm vụ (`task_material_plan` — F-RES-15/BR-33).</summary>
public sealed record TaskMaterialShortage(Guid MaterialId, decimal RequiredQuantity, decimal AllocatedQuantity);

/// <summary>
/// Ảnh chụp "đã cấp bao nhiêu vật tư cho nhiệm vụ" do Resource trả về. `Known = false` khi Resource
/// không trả lời được (fail-open: không chặn tình nguyện viên vì lỗi dịch vụ kho).
/// </summary>
public sealed record TaskMaterialAllocationSnapshot(bool Known, IReadOnlyList<TaskMaterialShortage> Shortages)
{
    public static TaskMaterialAllocationSnapshot Unknown() => new(false, []);

    public static TaskMaterialAllocationSnapshot From(IReadOnlyCollection<TaskMaterialAllocationLine> lines)
    {
        // Chỉ xét vật tư nhiệm vụ THẬT SỰ yêu cầu (required > 0) và chưa được cấp đủ.
        var shortages = lines
            .Where(line => line.RequiredQuantity > 0 && line.AllocatedQuantity < line.RequiredQuantity)
            .Select(line => new TaskMaterialShortage(line.MaterialId, line.RequiredQuantity, line.AllocatedQuantity))
            .ToList();
        return new(true, shortages);
    }
}

public sealed record TaskMaterialAllocationLine(Guid MaterialId, decimal RequiredQuantity, decimal AllocatedQuantity);

/// <summary>Một lý do chặn bắt đầu nhiệm vụ (mã + câu giải thích tiếng Anh như `detail` của ProblemDetails).</summary>
public sealed record TaskStartBlocker(string Code, string Detail);

/// <summary><c>TaskStartEligibility</c>: vì sao nhiệm vụ được / chưa được bắt đầu (12/11/2026).</summary>
public sealed record TaskStartEligibilityDto(
    Guid TaskId,
    string TaskType,
    string Status,
    DateTimeOffset? StartAt,
    int AcceptedMembers,
    int RequiredMembers,
    bool MaterialsKnown,
    IReadOnlyList<TaskMaterialShortageDto> MaterialShortages,
    bool CanStart,
    IReadOnlyList<TaskStartBlocker> Blockers);

public sealed record TaskMaterialShortageDto(Guid MaterialId, decimal RequiredQuantity, decimal AllocatedQuantity);

/// <summary>
/// Quy tắc bắt đầu việc (12/11/2026): nhiệm vụ chỉ được bắt đầu (duyệt vào ca) khi
///   • đã tới giờ bắt đầu (`startAt`),                         → <c>TASK_START_NOT_DUE</c>
///   • đã có đủ thành viên đã nhận việc (`requiredVolunteerCount`), → <c>TASK_NOT_ENOUGH_MEMBERS</c>
///   • đã phân bổ đủ vật tư theo kế hoạch của nhiệm vụ.          → <c>TASK_NOT_ENOUGH_MATERIALS</c>
/// Dùng chung cho: trả lời "vì sao chưa bắt đầu được" cho client và chặn `requestCheckIn` /
/// duyệt đơn vào ca. Vật tư lấy từ Resource; nếu Resource không phản hồi thì KHÔNG chặn
/// (`MaterialsKnown = false`) để lỗi dịch vụ kho không làm tê liệt ngày thi công.
/// </summary>
public sealed class TaskStartEligibilityService(
    ITaskRepository tasks,
    IAssignmentStore assignments,
    ITaskResourceGateway resource,
    TaskScopeAccess scopeAccess,
    TimeProvider? clock = null)
{
    public async Task<TaskStartEligibilityDto> HandleAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetByIdAsync(taskId, cancellationToken)
            ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
        await scopeAccess.GetWorkAreaForReadAsync(task.WorkAreaId, cancellationToken);
        return await EvaluateAsync(task, cancellationToken);
    }

    /// <summary>Tính từ một task đã nạp (dùng bên trong transaction check-in / duyệt đơn).</summary>
    public async Task<TaskStartEligibilityDto> EvaluateAsync(WorkTask task, CancellationToken cancellationToken = default)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var members = await AcceptedMembersAsync(task, cancellationToken);
        var allocation = task.Materials.Count == 0
            ? new TaskMaterialAllocationSnapshot(true, [])
            : await ReadMaterialsAsync(task, cancellationToken);

        var blockers = new List<TaskStartBlocker>();
        if (task.Status == TaskState.Open)
        {
            blockers.Add(new("TASK_NOT_PUBLISHED",
                "The task is not published yet: the work-area leader must mark it ready before work can start."));
        }
        else if (task.Status is not (TaskState.NotStarted or TaskState.InProgress))
        {
            blockers.Add(new("TASK_INVALID_STATE", "The task was submitted or closed, so it can no longer be started."));
        }

        if (task.StartAt is { } startAt && now < startAt)
        {
            blockers.Add(new("TASK_START_NOT_DUE",
                $"The task can only start at {startAt:O}; the current time is {now:O}."));
        }

        if (members < task.RequiredVolunteerCount)
        {
            blockers.Add(new("TASK_NOT_ENOUGH_MEMBERS",
                $"The task needs {task.RequiredVolunteerCount} volunteer(s); {members} accepted so far."));
        }

        if (allocation.Shortages.Count > 0)
        {
            blockers.Add(new("TASK_NOT_ENOUGH_MATERIALS",
                $"Allocate the required materials first: {allocation.Shortages.Count} material line(s) are short."));
        }

        return new TaskStartEligibilityDto(
            task.Id,
            task.Type.ToString().ToUpperInvariant(),
            task.Status.ToString().ToUpperInvariant(),
            task.StartAt,
            members,
            task.RequiredVolunteerCount,
            allocation.Known,
            allocation.Shortages
                .Select(row => new TaskMaterialShortageDto(row.MaterialId, row.RequiredQuantity, row.AllocatedQuantity))
                .ToList(),
            blockers.Count == 0,
            blockers);
    }

    /// <summary>Chặn bắt đầu: ném lỗi 409 đầu tiên gặp phải (mã cụ thể cho client).</summary>
    public async Task EnsureCanStartAsync(WorkTask task, CancellationToken cancellationToken = default)
    {
        var eligibility = await EvaluateAsync(task, cancellationToken);
        if (eligibility.Blockers.Count == 0) return;
        var blocker = eligibility.Blockers[0];
        throw new DomainRuleException(blocker.Code, blocker.Detail);
    }

    private async Task<int> AcceptedMembersAsync(WorkTask task, CancellationToken cancellationToken)
    {
        var rows = await assignments.ListAsync(task.Id, cancellationToken);
        return rows.Count(row => row.Status is AssignmentStatus.Accepted or AssignmentStatus.InProgress
            or AssignmentStatus.Completed);
    }

    private async Task<TaskMaterialAllocationSnapshot> ReadMaterialsAsync(WorkTask task, CancellationToken cancellationToken)
    {
        try
        {
            return TaskMaterialAllocationSnapshot.From(await resource.TaskMaterialsAsync(task.Id, cancellationToken));
        }
        catch (DomainRuleException exception) when (exception.Kind == DomainErrorKind.ServiceUnavailable)
        {
            // Fail-open: không chặn thi công vì dịch vụ kho tạm thời không phản hồi.
            return TaskMaterialAllocationSnapshot.Unknown();
        }
    }
}
