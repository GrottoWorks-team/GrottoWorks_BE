using FluentAssertions;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Tasks;
using Xunit;

namespace TaskExecution.UnitTests.Application;

public sealed class VolunteerSkillTests
{
    [Theory]
    [InlineData("EXPERT", true)]
    [InlineData("ADVANCED", true)]
    [InlineData("INTERMEDIATE", false)]
    [InlineData(null, false)]
    public async Task Assignment_uses_the_minimum_level_from_identity(string? actualLevel, bool allowed)
    {
        var fixture = new AssignmentTestFixture();
        var skillId = Guid.NewGuid();
        fixture.Task.ReplaceSkills([new TaskSkillRequirement(skillId, "ADVANCED", null)], null, DateTimeOffset.UtcNow);
        fixture.Identity.Items[fixture.VolunteerId] = fixture.Identity.Items[fixture.VolunteerId] with
        {
            SkillIds = [skillId], Skills = [new(skillId, actualLevel)]
        };
        if (allowed)
        {
            await fixture.Service.AssignAsync(fixture.Command());
            fixture.Store.Items.Should().ContainSingle();
        }
        else
        {
            Func<Task> assign = () => fixture.Service.AssignAsync(fixture.Command());
            (await assign.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("TASK_SKILL_MISMATCH");
            fixture.Store.Items.Should().BeEmpty();
        }
    }

    [Fact]
    public void A_level_without_the_required_skill_is_not_a_match()
    {
        var declared = Guid.NewGuid();
        var volunteer = new IdentityUser(Guid.NewGuid(), "Volunteer", "ACTIVE", "VOLUNTEER", Guid.NewGuid(), null,
            [declared], [new(declared, "EXPERT")]);
        VolunteerSkills.Meets(volunteer, Guid.NewGuid(), "BASIC").Should().BeFalse();
        VolunteerSkills.Meets(volunteer, declared, null).Should().BeTrue();
    }
}
