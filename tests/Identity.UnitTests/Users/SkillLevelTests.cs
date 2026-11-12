using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Identity.Api.Requests;
using Identity.Application.Users.ReplaceMySkills;
using Identity.Contracts.Events;
using Identity.Domain;
using Identity.Domain.Entities;
using Xunit;

namespace Identity.UnitTests.Users;

public sealed class SkillLevelTests
{
    [Theory]
    [InlineData("EXPERT", "ADVANCED", true)]
    [InlineData(" basic ", "BASIC", true)]
    [InlineData("BASIC", "INTERMEDIATE", false)]
    [InlineData(null, "BASIC", false)]
    [InlineData(null, null, true)]
    public void Minimum_level_uses_the_declared_order(string? actual, string? required, bool expected) =>
        SkillLevels.Meets(actual, required).Should().Be(expected);

    [Fact]
    public void Unsupported_level_is_not_silently_mapped()
    {
        var action = () => VolunteerSkill.Create(Guid.NewGuid(), Guid.NewGuid(), "Professional");
        action.Should().Throw<DomainException>().Which.Code.Should().Be("VOLUNTEER_SKILL_LEVEL_INVALID");
    }

    [Fact]
    public async Task Level_update_is_returned_and_legacy_request_preserves_it()
    {
        var users = new FakeUserStore();
        var roles = TestUsers.SeedRoles(users);
        var volunteer = TestUsers.Add(users, roles[RoleCodes.Volunteer], "level@example.test", Guid.NewGuid());
        var catalog = new FakeSkillStore();
        var skill = Skill.Create("ELECTRIC", "Electrical");
        catalog.Seed(skill);
        var events = new FakeIntegrationEventPublisher();
        var handler = new ReplaceMySkillsCommandHandler(new FakeCurrentUser(volunteer.UserId), users, catalog, events);
        var statusVersion = volunteer.StatusVersion;
        var profile = await handler.HandleAsync(new ReplaceMySkillsCommand([skill.SkillId],
            [new VolunteerSkillInput(skill.SkillId, " advanced ")]));
        profile.Skills!.Single().SkillLevel.Should().Be("ADVANCED");
        profile = await handler.HandleAsync(new ReplaceMySkillsCommand([skill.SkillId]));
        profile.Skills!.Single().SkillLevel.Should().Be("ADVANCED");
        events.Events.Should().ContainSingle();

        await handler.HandleAsync(new([skill.SkillId], [new VolunteerSkillInput(skill.SkillId, "BASIC")]));
        await handler.HandleAsync(new([]));
        volunteer.StatusVersion.Should().Be(statusVersion);
        events.Events.Should().HaveCount(3).And.OnlyContain(evt =>
            evt.EventType == IdentityUserEligibilityChanged.EventType && evt.AggregateId == volunteer.UserId);
    }

    [Fact]
    public void Request_cannot_mix_legacy_ids_with_detailed_skills()
    {
        var request = new ReplaceSkillsRequest { SkillIds = [], Skills = [] };
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), errors, true).Should().BeFalse();
    }
}
