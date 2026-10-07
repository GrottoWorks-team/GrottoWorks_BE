using FluentAssertions;
using Identity.Application.Skills.CreateSkill;
using Identity.Application.Skills.ListSkills;
using Identity.Application.Skills.UpdateSkill;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;
using Xunit;

namespace Identity.UnitTests.Skills;

public sealed class SkillHandlerTests
{
    [Fact]
    public async Task CreateSkill_normalizes_code_and_creates_active_skill()
    {
        var store = new FakeSkillStore();

        var skill = await new CreateSkillCommandHandler(store).HandleAsync(
            new CreateSkillCommand("  sound ", "Âm thanh", "Vận hành loa và micro."),
            CancellationToken.None);

        skill.Code.Should().Be("SOUND");
        skill.Status.Should().Be(SkillStatus.Active);
        store.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateSkill_rejects_a_duplicate_code()
    {
        var store = new FakeSkillStore();
        store.Seed(Skill.Create("SOUND", "Âm thanh"));

        var act = async () => await new CreateSkillCommandHandler(store).HandleAsync(
            new CreateSkillCommand("sound", "Another name", null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "SKILL_CODE_ALREADY_EXISTS");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateSkill_rejects_an_invalid_code(string code)
    {
        var act = async () => await new CreateSkillCommandHandler(new FakeSkillStore()).HandleAsync(
            new CreateSkillCommand(code, "Name", null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "SKILL_CODE_INVALID");
    }

    [Fact]
    public async Task UpdateSkill_updates_fields_and_deactivates()
    {
        var store = new FakeSkillStore();
        var existing = Skill.Create("SOUND", "Âm thanh");
        store.Seed(existing);

        var updated = await new UpdateSkillCommandHandler(store).HandleAsync(
            new UpdateSkillCommand(existing.SkillId, "Ánh sáng", "Đèn chiếu sáng.", SkillStatus.Inactive),
            CancellationToken.None);

        updated.Name.Should().Be("Ánh sáng");
        updated.Status.Should().Be(SkillStatus.Inactive);
    }

    [Fact]
    public async Task UpdateSkill_keeps_fields_when_null_passed()
    {
        var store = new FakeSkillStore();
        var existing = Skill.Create("SOUND", "Âm thanh");
        store.Seed(existing);

        var updated = await new UpdateSkillCommandHandler(store).HandleAsync(
            new UpdateSkillCommand(existing.SkillId, null, null, null),
            CancellationToken.None);

        updated.Name.Should().Be("Âm thanh");
        updated.Status.Should().Be(SkillStatus.Active);
    }

    [Fact]
    public async Task UpdateSkill_reports_not_found()
    {
        var act = async () => await new UpdateSkillCommandHandler(new FakeSkillStore()).HandleAsync(
            new UpdateSkillCommand(Guid.NewGuid(), null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(exception => exception.Code == "SKILL_NOT_FOUND");
    }

    [Fact]
    public async Task ListSkills_filters_by_status_and_orders_by_name()
    {
        var store = new FakeSkillStore();
        store.Seed(Skill.Create("ELECTRIC", "Electric"));
        store.Seed(Skill.Create("ANNOUNCE", "Announcing"));
        var inactive = Skill.Create("LIGHTING", "Lighting");
        inactive.Deactivate();
        store.Seed(inactive);

        var handler = new ListSkillsQueryHandler(store);

        var all = await handler.HandleAsync(new ListSkillsQuery(), CancellationToken.None);
        all.Select(skill => skill.Name).Should().BeInAscendingOrder();

        var activeOnly = await handler.HandleAsync(
            new ListSkillsQuery(SkillStatus.Active),
            CancellationToken.None);
        activeOnly.Should().OnlyContain(skill => skill.Status == SkillStatus.Active);

        var inactiveOnly = await handler.HandleAsync(
            new ListSkillsQuery(SkillStatus.Inactive),
            CancellationToken.None);
        inactiveOnly.Should().ContainSingle().Which.Code.Should().Be("LIGHTING");
    }
}