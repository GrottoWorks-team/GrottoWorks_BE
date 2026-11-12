using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;

namespace TaskExecution.Application.Support;

/// <summary>
/// Nhiệm vụ có đang được khu của nó xin hỗ trợ (yêu cầu hỗ trợ đã phát hành) hay không — luật 13/11/2026:
/// tình nguyện viên khu khác chỉ tham gia được việc mà khu đó đang xin hỗ trợ.
/// </summary>
public interface ITaskSupportCoverage
{
    Task<bool> IsTaskRequestingSupportAsync(Guid taskId, CancellationToken cancellationToken);
}

public sealed class TaskSupportCoverage(ISupportRequestRepository requests) : ITaskSupportCoverage
{
    public Task<bool> IsTaskRequestingSupportAsync(Guid taskId, CancellationToken cancellationToken) =>
        requests.IsTaskRequestingSupportAsync(taskId, cancellationToken);
}

/// <summary>
/// Luật phân công theo khu vực (13/11/2026): trưởng khu chỉ phân công được tình nguyện viên **thuộc cộng đoàn
/// của khu mình**. Thiếu người thì tạo yêu cầu hỗ trợ để khu khác gửi người sang (luồng support đi đường riêng).
/// Khu không gắn cộng đoàn (khu cấp giáo xứ) thì giữ nguyên hành vi cũ: mọi tình nguyện viên trong giáo xứ.
/// </summary>
public static class AssignmentCommunity
{
    public const string OutsideAreaCode = "TASK_ASSIGNEE_OUTSIDE_AREA";

    public static void EnsureVolunteerBelongs(WorkAreaScope area, Guid? volunteerCommunityId)
    {
        if (!area.CommunityId.HasValue) return;
        if (volunteerCommunityId == area.CommunityId) return;
        throw new DomainRuleException(OutsideAreaCode,
            "Only volunteers of this work area's community can be assigned. Create a support request so another area can send volunteers.");
    }
}
