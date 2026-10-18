using FluentAssertions;
using Identity.Domain;
using Identity.Domain.Entities;
using Identity.Domain.Enums;
using Xunit;

namespace Identity.UnitTests.Domain;

/// <summary>Domain rules added in S2: F-IDN-07 skills/availability, F-IDN-08 lifecycle, BR-67, version.</summary>
public sealed class AppUserS2Tests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private static AppUser NewVolunteer() =>
        AppUser.Create("Nguyen Van A", "a@example.com", null, Guid.NewGuid(), Role.Create(RoleCodes.Volunteer, "Volunteer"), Now);

    private static DomainException Thrown(Action act) =>
        act.Should().Throw<DomainException>().Which;

    [Fact]
    public void ReplaceSkills_keeps_existing_links_adds_new_and_removes_missing()
    {
        var user = NewVolunteer();
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();
        var add = Guid.NewGuid();
        user.ReplaceSkills([keep, drop], Now);
        var keptLink = user.VolunteerSkills.Single(link => link.SkillId == keep);

        user.ReplaceSkills([keep, add], Now);

        user.VolunteerSkills.Select(link => link.SkillId).Should().BeEquivalentTo([keep, add]);
        user.VolunteerSkills.Should().Contain(keptLink, "unchanged links keep their row, level and note");
    }

    [Fact]
    public void ReplaceSkills_with_empty_list_clears_the_skills()
    {
        var user = NewVolunteer();
        user.ReplaceSkills([Guid.NewGuid()], Now);

        user.ReplaceSkills([], Now);

        user.VolunteerSkills.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceSkills_rejects_duplicates_empty_ids_and_too_many()
    {
        var user = NewVolunteer();
        var skill = Guid.NewGuid();

        Thrown(() => user.ReplaceSkills([skill, skill], Now)).Code.Should().Be("VOLUNTEER_SKILL_DUPLICATE");
        Thrown(() => user.ReplaceSkills([Guid.Empty], Now)).Code.Should().Be("VOLUNTEER_SKILL_INVALID");
        Thrown(() => user.ReplaceSkills(Enumerable.Range(0, AppUser.MaxSkills + 1).Select(_ => Guid.NewGuid()).ToList(), Now))
            .Code.Should().Be("VOLUNTEER_SKILL_LIMIT");
    }

    [Fact]
    public void ReplaceAvailability_stores_sorted_utc_slots()
    {
        var user = NewVolunteer();
        var plus7 = TimeSpan.FromHours(7);

        user.ReplaceAvailability(
        [
            new AvailabilitySlot(new DateTimeOffset(2026, 12, 20, 18, 0, 0, plus7), new DateTimeOffset(2026, 12, 20, 21, 0, 0, plus7), " evening "),
            new AvailabilitySlot(new DateTimeOffset(2026, 12, 20, 8, 0, 0, plus7), new DateTimeOffset(2026, 12, 20, 11, 0, 0, plus7), null)
        ], Now);

        user.VolunteerAvailabilities.Should().HaveCount(2);
        user.VolunteerAvailabilities[0].AvailableFrom.Should().Be(new DateTimeOffset(2026, 12, 20, 1, 0, 0, TimeSpan.Zero));
        user.VolunteerAvailabilities.Should().OnlyContain(slot => slot.AvailableFrom.Offset == TimeSpan.Zero);
        user.VolunteerAvailabilities[1].AvailabilityNote.Should().Be("evening");
    }

    [Fact]
    public void ReplaceAvailability_rejects_invalid_range_br70()
    {
        var user = NewVolunteer();
        var start = Now.AddDays(1);

        Thrown(() => user.ReplaceAvailability([new AvailabilitySlot(start, start, null)], Now))
            .Code.Should().Be("AVAILABILITY_INVALID_RANGE");
    }

    [Fact]
    public void ReplaceAvailability_rejects_overlap_but_allows_adjacent_slots()
    {
        var user = NewVolunteer();
        var start = Now.AddDays(1);

        Thrown(() => user.ReplaceAvailability(
        [
            new AvailabilitySlot(start, start.AddHours(3), null),
            new AvailabilitySlot(start.AddHours(2), start.AddHours(4), null)
        ], Now)).Code.Should().Be("AVAILABILITY_OVERLAP");

        user.ReplaceAvailability(
        [
            new AvailabilitySlot(start, start.AddHours(3), null),
            new AvailabilitySlot(start.AddHours(3), start.AddHours(4), null)
        ], Now);

        user.VolunteerAvailabilities.Should().HaveCount(2);
    }

    [Fact]
    public void ReplaceAvailability_failure_keeps_previous_slots()
    {
        var user = NewVolunteer();
        var start = Now.AddDays(1);
        user.ReplaceAvailability([new AvailabilitySlot(start, start.AddHours(1), null)], Now);

        Thrown(() => user.ReplaceAvailability([new AvailabilitySlot(start.AddHours(2), start.AddHours(1), null)], Now));

        user.VolunteerAvailabilities.Should().ContainSingle();
    }

    [Fact]
    public void Lock_twice_and_unlock_when_not_locked_are_state_conflicts()
    {
        var user = NewVolunteer();

        Thrown(() => user.Unlock(Now)).Code.Should().Be("USER_NOT_LOCKED");

        user.Lock(Now);
        Thrown(() => user.Lock(Now)).Code.Should().Be("USER_ALREADY_LOCKED");
        Thrown(() => user.Deactivate(Now)).Code.Should().Be("USER_LOCKED_USE_UNLOCK");
        Thrown(() => user.Activate(Now)).Code.Should().Be("USER_LOCKED_USE_UNLOCK");

        user.Unlock(Now);
        user.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public void AssignRole_checks_br67_against_the_final_role_and_parish()
    {
        var admin = AppUser.Create("Admin", "admin@example.com", null, null, Role.Create(RoleCodes.Admin, "Admin"), Now);
        var leader = Role.Create(RoleCodes.Leader, "Leader");

        Thrown(() => admin.AssignRole(leader, null, Now)).Code.Should().Be("USER_PARISH_REQUIRED");
        admin.Role!.Code.Should().Be(RoleCodes.Admin, "a failed assignment changes nothing");

        var parishId = Guid.NewGuid();
        admin.AssignRole(leader, parishId, Now);

        admin.Role.Code.Should().Be(RoleCodes.Leader);
        admin.ParishId.Should().Be(parishId);
    }

    [Fact]
    public void EnsureVersion_accepts_missing_or_matching_version_only()
    {
        var user = NewVolunteer();

        user.EnsureVersion(null);
        user.EnsureVersion(user.Version);
        Thrown(() => user.EnsureVersion(user.Version + 1)).Code.Should().Be("USER_VERSION_MISMATCH");
    }

    [Fact]
    public void ChangeEmail_normalizes()
    {
        var user = NewVolunteer();

        user.ChangeEmail("  New@Example.COM ", Now);

        user.Email.Should().Be("new@example.com");
    }

    [Fact]
    public void PasswordResetToken_is_single_use_and_expires()
    {
        var token = PasswordResetToken.Create(Guid.NewGuid(), "hash", Now, Now.AddMinutes(30));

        token.IsUsable(Now.AddMinutes(29)).Should().BeTrue();
        token.IsUsable(Now.AddMinutes(30)).Should().BeFalse("expiry is exclusive");

        token.MarkUsed(Now);
        token.IsUsable(Now).Should().BeFalse();

        var act = () => PasswordResetToken.Create(Guid.NewGuid(), "hash", Now, Now);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("PASSWORD_RESET_INVALID_TTL");
    }
}
