using FluentAssertions;
using Identity.Application.Users.ReplaceMyAvailability;
using Identity.Application.Users.ReplaceMySkills;
using Identity.Domain;
using Identity.Domain.Entities;
using Xunit;

namespace Identity.UnitTests.Users;

/// <summary>F-IDN-07: replaceMySkills / replaceMyAvailability.</summary>
public sealed class VolunteerSkillsAvailabilityTests
{
    private readonly FakeUserStore _users = new();
    private readonly FakeSkillStore _skills = new();
    private readonly AppUser _volunteer;
    private readonly Skill _electric = Skill.Create("ELECTRIC", "Electrical");
    private readonly Skill _paint = Skill.Create("PAINT", "Painting");

    public VolunteerSkillsAvailabilityTests()
    {
        var roles = TestUsers.SeedRoles(_users);
        _volunteer = TestUsers.Add(_users, roles[RoleCodes.Volunteer], "v@example.com", Guid.NewGuid());
        _skills.Seed(_electric);
        _skills.Seed(_paint);
    }

    private ReplaceMySkillsCommandHandler SkillsHandler() =>
        new(new FakeCurrentUser(_volunteer.UserId), _users, _skills);

    [Fact]
    public async Task ReplaceMySkills_replaces_the_set_and_returns_the_profile()
    {
        var profile = await SkillsHandler().HandleAsync(new ReplaceMySkillsCommand([_electric.SkillId, _paint.SkillId]));

        profile.SkillIds.Should().BeEquivalentTo([_electric.SkillId, _paint.SkillId]);
        _users.SaveChangesCount.Should().Be(1);

        profile = await SkillsHandler().HandleAsync(new ReplaceMySkillsCommand([_paint.SkillId]));
        profile.SkillIds.Should().Equal(_paint.SkillId);
    }

    [Fact]
    public async Task ReplaceMySkills_rejects_unknown_skill()
    {
        var act = () => SkillsHandler().HandleAsync(new ReplaceMySkillsCommand([_electric.SkillId, Guid.NewGuid()]));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("VOLUNTEER_SKILL_UNKNOWN");
        _volunteer.VolunteerSkills.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplaceMySkills_rejects_newly_declaring_an_inactive_skill_but_keeps_an_existing_one()
    {
        await SkillsHandler().HandleAsync(new ReplaceMySkillsCommand([_electric.SkillId]));
        _electric.Deactivate();
        _paint.Deactivate();

        var keep = await SkillsHandler().HandleAsync(new ReplaceMySkillsCommand([_electric.SkillId]));
        keep.SkillIds.Should().Equal(_electric.SkillId);

        var act = () => SkillsHandler().HandleAsync(new ReplaceMySkillsCommand([_electric.SkillId, _paint.SkillId]));
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("VOLUNTEER_SKILL_INACTIVE");
    }

    [Fact]
    public async Task ReplaceMySkills_reports_duplicates()
    {
        var act = () => SkillsHandler().HandleAsync(new ReplaceMySkillsCommand([_paint.SkillId, _paint.SkillId]));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("VOLUNTEER_SKILL_DUPLICATE");
    }

    [Fact]
    public async Task ReplaceMyAvailability_replaces_slots()
    {
        var start = new DateTimeOffset(2026, 12, 20, 1, 0, 0, TimeSpan.Zero);
        var handler = new ReplaceMyAvailabilityCommandHandler(new FakeCurrentUser(_volunteer.UserId), _users);

        var profile = await handler.HandleAsync(new ReplaceMyAvailabilityCommand(
        [
            new AvailabilitySlot(start, start.AddHours(4), "morning"),
            new AvailabilitySlot(start.AddDays(1), start.AddDays(1).AddHours(2), null)
        ]));

        profile.Availability.Should().HaveCount(2);
        profile.Availability[0].Note.Should().Be("morning");

        profile = await handler.HandleAsync(new ReplaceMyAvailabilityCommand([]));
        profile.Availability.Should().BeEmpty();
        _users.SaveChangesCount.Should().Be(2);
    }

    [Fact]
    public async Task ReplaceMyAvailability_rejects_overlapping_slots()
    {
        var start = new DateTimeOffset(2026, 12, 20, 1, 0, 0, TimeSpan.Zero);
        var handler = new ReplaceMyAvailabilityCommandHandler(new FakeCurrentUser(_volunteer.UserId), _users);

        var act = () => handler.HandleAsync(new ReplaceMyAvailabilityCommand(
        [
            new AvailabilitySlot(start, start.AddHours(4), null),
            new AvailabilitySlot(start.AddHours(1), start.AddHours(2), null)
        ]));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("AVAILABILITY_OVERLAP");
        _users.SaveChangesCount.Should().Be(0);
    }
}
