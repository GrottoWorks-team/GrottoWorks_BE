using Resource.Application.Scope;
using Resource.Domain.Inventory;
using Resource.Domain.Scope;
using TaskExecution.Contracts.Events;

namespace Resource.UnitTests.Scope;

/// <summary>
/// 13/11/2026 — nhu cầu vật tư của khu phải bao gồm nhu cầu của nhiệm vụ.
/// Trước đây khu khai 10 nhưng nhiệm vụ cần 20 vẫn báo "Đủ vật tư" vì nhu cầu khu không biết tới nhiệm vụ.
/// </summary>
public sealed class TaskMaterialAreaRequirementSyncTests
{
    private static readonly Guid AreaId = Guid.NewGuid();
    private static readonly Guid MaterialId = Guid.NewGuid();
    private static readonly Guid ParishId = Guid.NewGuid();
    private static readonly Guid SeasonId = Guid.NewGuid();

    [Fact]
    public async Task Task_demand_raises_the_area_requirement()
    {
        var plans = new FakeTaskMaterialPlanStore();
        var requirements = new FakeRequirementStore();
        requirements.Add(MaterialRequirement.Create(AreaId, MaterialId, 10, 0, null, DateTimeOffset.UtcNow));
        var sync = new TaskMaterialAreaRequirementSync(plans, requirements);
        await PlanAsync(plans, sync, requiredQuantity: 20);

        requirements.Requirements.Should().ContainSingle();
        requirements.Requirements[0].RequiredQuantity.Should().Be(20);
        requirements.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Manually_higher_requirement_is_not_lowered_by_task_demand()
    {
        var plans = new FakeTaskMaterialPlanStore();
        var requirements = new FakeRequirementStore();
        requirements.Add(MaterialRequirement.Create(AreaId, MaterialId, 25, 0, null, DateTimeOffset.UtcNow));
        var sync = new TaskMaterialAreaRequirementSync(plans, requirements);
        await PlanAsync(plans, sync, requiredQuantity: 20);

        requirements.Requirements[0].RequiredQuantity.Should().Be(25);
        requirements.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task Missing_requirement_is_created_from_task_demand()
    {
        var plans = new FakeTaskMaterialPlanStore();
        var requirements = new FakeRequirementStore();
        var sync = new TaskMaterialAreaRequirementSync(plans, requirements);
        await PlanAsync(plans, sync, requiredQuantity: 20);

        var created = requirements.Requirements.Should().ContainSingle().Subject;
        created.WorkAreaId.Should().Be(AreaId);
        created.MaterialId.Should().Be(MaterialId);
        created.RequiredQuantity.Should().Be(20);
        created.ExistingQuantity.Should().Be(0);
        created.Note.Should().Be(TaskMaterialAreaRequirementSync.TaskDemandNote);
        created.Status.Should().Be(RequirementStatus.Active);
    }

    [Fact]
    public async Task Cancelled_tasks_do_not_count_towards_area_demand()
    {
        var plans = new FakeTaskMaterialPlanStore();
        var requirements = new FakeRequirementStore();
        var sync = new TaskMaterialAreaRequirementSync(plans, requirements);
        var cancelled = await PlanAsync(plans, sync, requiredQuantity: 8);
        plans.CancelledTasks.Add(cancelled);
        await PlanAsync(plans, sync, requiredQuantity: 20);

        requirements.Requirements[0].RequiredQuantity.Should().Be(20);
    }

    private static async Task<Guid> PlanAsync(FakeTaskMaterialPlanStore plans, TaskMaterialAreaRequirementSync sync,
        decimal requiredQuantity)
    {
        var taskId = Guid.NewGuid();
        await new TaskMaterialPlanProjector(plans, sync).ApplyAsync(taskId, AreaId, ParishId, SeasonId, 1,
            [new TaskMaterialSnapshot(MaterialId, requiredQuantity, null)], default);
        return taskId;
    }
}
