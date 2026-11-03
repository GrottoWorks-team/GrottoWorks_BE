using FluentAssertions;
using Identity.Application.Users.GetInternalUsers;
using Identity.Domain;
using Identity.Domain.Entities;
using Xunit;

namespace Identity.UnitTests.Users;

public sealed class InternalVolunteerDirectoryTests
{
    [Fact]
    public async Task Candidates_are_active_volunteers_in_the_requested_parish()
    {
        var store = new FakeUserStore();
        var roles = TestUsers.SeedRoles(store);
        var parish = Guid.NewGuid();
        var volunteer = TestUsers.Add(store, roles[RoleCodes.Volunteer], "candidate@example.test", parish);
        TestUsers.Add(store, roles[RoleCodes.Leader], "leader@example.test", parish);
        TestUsers.Add(store, roles[RoleCodes.Volunteer], "other@example.test", Guid.NewGuid());
        var locked = TestUsers.Add(store, roles[RoleCodes.Volunteer], "locked@example.test", parish);
        locked.Deactivate(DateTimeOffset.UtcNow);
        var result = await new GetInternalVolunteersQueryHandler(store).HandleAsync(parish, null, null, 1, 100);
        result.TotalItems.Should().Be(1);
        result.Items.Single().UserId.Should().Be(volunteer.UserId);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    public async Task Invalid_pagination_is_rejected(int page, int size)
    {
        var handler = new GetInternalVolunteersQueryHandler(new FakeUserStore());
        var action = () => handler.HandleAsync(Guid.NewGuid(), null, null, page, size);
        await action.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public void Lookup_returns_skill_levels_and_utc_availability_without_contact_fields()
    {
        var store = new FakeUserStore();
        var role = TestUsers.SeedRoles(store)[RoleCodes.Volunteer];
        var user = TestUsers.Add(store, role, "private@example.test", Guid.NewGuid());
        var skill = Guid.NewGuid();
        user.ReplaceSkillLevels([new VolunteerSkillInput(skill, "EXPERT")], DateTimeOffset.UtcNow);
        var start = DateTimeOffset.Parse("2026-12-01T08:00:00Z");
        user.ReplaceAvailability([new AvailabilitySlot(start, start.AddHours(2), null)], DateTimeOffset.UtcNow);
        var result = InternalUserDto.From(user);
        result.Skills.Single().SkillLevel.Should().Be("EXPERT");
        result.Availability.Single().AvailableFrom.Should().Be(start);
        typeof(InternalUserDto).GetProperties().Select(property => property.Name)
            .Should().NotContain(new[] { "Email", "Phone", "PasswordHash", "RefreshToken" });
    }
}
