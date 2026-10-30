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
        db.Model.FindEntityType(typeof(SupportPositionSkill))!.FindPrimaryKey()!.Properties.Should().HaveCount(2);
        db.Model.FindEntityType(typeof(SupportMaterial))!.GetIndexes().Should().Contain(i=>i.IsUnique);
        db.Database.HasPendingModelChanges().Should().BeFalse();
    }
}
