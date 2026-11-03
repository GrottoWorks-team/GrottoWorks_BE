using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskExecution.Domain.Support;
using TaskExecution.Infrastructure.Data;

namespace TaskExecution.UnitTests.Infrastructure;

public sealed class SupportRequestModelTests
{
    [Fact]
    public void Support_graph_maps_owned_membership_unique_material_and_concurrency()
    {
        using var db=new TaskExecutionDbContext(new DbContextOptionsBuilder<TaskExecutionDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=test;Password=test").Options);
        var request=db.Model.FindEntityType(typeof(SupportRequest))!;
        request.FindProperty(nameof(SupportRequest.Version))!.IsConcurrencyToken.Should().BeTrue();
        var position=db.Model.FindEntityType(typeof(SupportPosition))!;
        position.FindProperty(nameof(SupportPosition.TaskId))!.IsNullable.Should().BeTrue();
        position.GetForeignKeys().Should().Contain(key=>key.PrincipalEntityType.ClrType==typeof(TaskExecution.Domain.Tasks.WorkTask)
            && key.DeleteBehavior==DeleteBehavior.Restrict);
        db.Model.FindEntityType(typeof(SupportPositionSkill))!.FindPrimaryKey()!.Properties.Should().HaveCount(2);
        db.Model.FindEntityType(typeof(SupportMaterial))!.GetIndexes().Should().Contain(i=>i.IsUnique);
        var member=db.Model.FindEntityType(typeof(SupportResponseMember))!;
        member.GetIndexes().Should().Contain(index=>index.IsUnique && index.GetFilter()!=null && index.Properties.Any(property=>property.Name==nameof(SupportResponseMember.RequestId)));
        member.GetForeignKeys().Should().Contain(key=>key.PrincipalEntityType.ClrType==typeof(SupportPosition) && key.Properties.Count==2);
        db.Database.HasPendingModelChanges().Should().BeFalse();
    }
}
