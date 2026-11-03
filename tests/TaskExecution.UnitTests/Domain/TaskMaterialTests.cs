using FluentAssertions;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;
using Xunit;

namespace TaskExecution.UnitTests.Domain;

public sealed class TaskMaterialTests
{
    [Fact]
    public void Replacement_preserves_row_identity_and_noop_is_order_independent()
    {
        var task = Create();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        task.ReplaceMaterials([new(first, 2, " note "), new(second, 3, " ")], 1, DateTimeOffset.UtcNow).Should().BeTrue();
        var rowId = task.Materials.Single(row => row.MaterialId == first).Id;
        task.Materials.Single(row => row.MaterialId == first).Note.Should().Be("note");
        task.ReplaceMaterials([new(second, 3, null), new(first, 2, "note")], 2, DateTimeOffset.UtcNow).Should().BeFalse();
        task.Version.Should().Be(2);
        task.ReplaceMaterials([new(first, 4, null)], 2, DateTimeOffset.UtcNow).Should().BeTrue();
        task.Materials.Should().ContainSingle().Which.Id.Should().Be(rowId);
        task.Materials.Single().RequiredQuantity.Should().Be(4);
        task.ReplaceMaterials([], 3, DateTimeOffset.UtcNow).Should().BeTrue();
        task.Materials.Should().BeEmpty();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("10000000000")]
    public void Quantities_must_fit_the_logical_erd_precision(string value)
    {
        var quantity = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        Action replace = () => Create().ReplaceMaterials([new(Guid.NewGuid(), quantity, null)], null, DateTimeOffset.UtcNow);
        replace.Should().Throw<DomainRuleException>().Which.Code.Should().Be("TASK_MATERIAL_QUANTITY_INVALID");
    }

    [Fact]
    public void Duplicates_stale_version_and_non_open_state_are_rejected()
    {
        var task = Create();
        var id = Guid.NewGuid();
        Action duplicate = () => task.ReplaceMaterials([new(id, 1, null), new(id, 2, null)], null, DateTimeOffset.UtcNow);
        duplicate.Should().Throw<DomainRuleException>().Which.Code.Should().Be("TASK_MATERIAL_DUPLICATE");
        Action stale = () => task.ReplaceMaterials([], 2, DateTimeOffset.UtcNow);
        stale.Should().Throw<DomainRuleException>().Which.Kind.Should().Be(DomainErrorKind.PreconditionFailed);
        task.MarkReady(null, DateTimeOffset.UtcNow);
        Action closed = () => task.ReplaceMaterials([], null, DateTimeOffset.UtcNow);
        closed.Should().Throw<DomainRuleException>().Which.Code.Should().Be("TASK_INVALID_STATE");
    }
    private static WorkTask Create() => WorkTask.CreateOpen(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Task",
        null, null, TaskPriority.Medium, 60, 1, null, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow);
}
