using Resource.Application.Requirements;
using Resource.Domain.Inventory;

namespace Resource.Application.Scope;

/// <summary>
/// 13/11/2026 — đồng bộ nhu cầu vật tư của KHU theo nhu cầu của các NHIỆM VỤ.
///
/// Trước đây `material_requirement` chỉ do người dùng khai; khi trưởng khu khai nhu cầu 10 nhưng nhiệm vụ
/// cần 20 thì mọi tính toán thiếu hụt (badge "Đủ vật tư", "Nhu cầu còn thiếu", quyên góp) đều bỏ qua 20
/// ⇒ báo đủ sai. Nay mỗi lần `task_material_plan` thay đổi, nhu cầu của khu được **nâng** lên bằng tổng nhu
/// cầu các nhiệm vụ chưa huỷ của khu đó.
///
/// Quy tắc: **tổng** nhu cầu các nhiệm vụ chưa huỷ; chỉ nâng, không hạ (con số khu tự khai vẫn được giữ,
/// hạ bằng cách PATCH requirement). Việc này idempotent theo `TaskMaterialPlan.SourceVersion` vì chỉ chạy
/// khi plan thực sự thay đổi.
/// </summary>
public sealed class TaskMaterialAreaRequirementSync(
    ITaskMaterialPlanStore plans,
    IMaterialRequirementStore requirements)
{
    public const string TaskDemandNote = "Tổng nhu cầu từ nhiệm vụ của khu";

    public async Task SyncAsync(Guid workAreaId, IReadOnlyCollection<Guid> materialIds, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (materialIds.Count == 0) return;
        var totals = (await plans.GetAreaTotalsAsync(workAreaId, cancellationToken))
            .Where(total => materialIds.Contains(total.MaterialId))
            .ToList();
        if (totals.Count == 0) return;

        var existing = (await requirements.ListByWorkAreaForUpdateAsync(workAreaId, cancellationToken))
            .ToDictionary(requirement => requirement.MaterialId);
        var changed = false;
        foreach (var total in totals)
        {
            if (existing.TryGetValue(total.MaterialId, out var requirement))
            {
                changed |= requirement.RaiseRequiredQuantity(total.RequiredQuantity, now);
                continue;
            }

            requirements.Add(MaterialRequirement.Create(workAreaId, total.MaterialId, total.RequiredQuantity, 0,
                TaskDemandNote, now));
            changed = true;
        }

        if (changed) await requirements.SaveChangesAsync(cancellationToken);
    }
}
