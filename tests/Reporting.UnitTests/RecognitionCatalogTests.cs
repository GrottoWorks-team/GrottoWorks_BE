using Reporting.Application.Recognition;
using Reporting.Domain;
using Reporting.Domain.Recognition;

namespace Reporting.UnitTests;

public sealed class RecognitionCatalogTests
{
    private static readonly DateOnly Jan1 = new(2026, 12, 1);

    private readonly FakeRecognitionCatalogStore _store = new();

    private RecognitionRuleHandler Rules => new(_store);

    private ContributionTypeHandler Types => new(_store);

    [Fact]
    public async Task System_types_are_seeded_and_new_codes_are_normalised_and_unique()
    {
        (await Types.ListAsync(null)).Should().HaveCount(6).And.OnlyContain(type => type.System);

        var created = await Types.CreateAsync(new CreateContributionTypeCommand(" emergency_help ", "Hỗ trợ khẩn cấp", null));
        created.Code.Should().Be("EMERGENCY_HELP");
        created.System.Should().BeFalse();

        var duplicate = () => Types.CreateAsync(new CreateContributionTypeCommand("EMERGENCY_HELP", "X", null));
        (await duplicate.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("CONTRIBUTION_TYPE_CODE_EXISTS");

        var invalid = () => Types.CreateAsync(new CreateContributionTypeCommand("bad code!", "X", null));
        (await invalid.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("CONTRIBUTION_TYPE_CODE_INVALID");
    }

    [Fact]
    public async Task Type_update_changes_only_sent_fields_and_can_deactivate()
    {
        var type = _store.Type(ContributionType.TaskCompletion);

        var updated = await Types.UpdateAsync(new UpdateContributionTypeCommand(type.ContributionTypeId, null, "Mô tả mới", ContributionTypeStatus.Inactive));

        updated.Name.Should().Be("Hoàn thành task");
        updated.Description.Should().Be("Mô tả mới");
        updated.Status.Should().Be(ContributionTypeStatus.Inactive);
        (await Types.ListAsync(ContributionTypeStatus.Active)).Should().HaveCount(5);
    }

    [Fact]
    public async Task New_rule_starts_at_version_one_and_names_are_unique()
    {
        var typeId = _store.Type(ContributionType.TaskCompletion).ContributionTypeId;

        var rule = await Rules.CreateAsync(new CreateRecognitionRuleCommand(typeId, "Task hoàn thành", 10, null, Jan1, null, null));

        rule.RuleVersion.Should().Be(1);
        rule.ContributionTypeCode.Should().Be(ContributionType.TaskCompletion);
        rule.Status.Should().Be(RecognitionRuleStatus.Active);

        var again = () => Rules.CreateAsync(new CreateRecognitionRuleCommand(typeId, "Task hoàn thành", 15, null, Jan1, null, null));
        (await again.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("RECOGNITION_RULE_NAME_EXISTS");
    }

    [Fact]
    public async Task Changing_points_creates_the_next_version_and_ends_the_previous_one()
    {
        var typeId = _store.Type(ContributionType.TaskCompletion).ContributionTypeId;
        var v1 = await Rules.CreateAsync(new CreateRecognitionRuleCommand(typeId, "Task hoàn thành", 10, "Gốc", Jan1, null, null));

        var v2 = await Rules.CreateAsync(new CreateRecognitionRuleCommand(null, null, 12, null, Jan1.AddDays(10), null, v1.Id));

        v2.RuleVersion.Should().Be(2);
        v2.Name.Should().Be("Task hoàn thành");
        v2.Points.Should().Be(12);
        v2.Description.Should().Be("Gốc");
        var previous = _store.Rules.Single(rule => rule.RecognitionRuleId == v1.Id);
        previous.EffectiveTo.Should().Be(Jan1.AddDays(9));
        previous.AppliesOn(Jan1.AddDays(9)).Should().BeTrue();
        previous.AppliesOn(Jan1.AddDays(10)).Should().BeFalse();

        var fromOld = () => Rules.CreateAsync(new CreateRecognitionRuleCommand(null, null, 20, null, Jan1.AddDays(20), null, v1.Id));
        (await fromOld.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("RECOGNITION_RULE_NOT_LATEST");

        var backwards = () => Rules.CreateAsync(new CreateRecognitionRuleCommand(null, null, 20, null, Jan1.AddDays(10), null, v2.Id));
        (await backwards.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("RECOGNITION_RULE_PERIOD_INVALID");

        var renamed = () => Rules.CreateAsync(new CreateRecognitionRuleCommand(null, "Khác", 20, null, Jan1.AddDays(30), null, v2.Id));
        (await renamed.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("RECOGNITION_RULE_NAME_MISMATCH");
    }

    [Fact]
    public async Task Rule_validation()
    {
        var typeId = _store.Type(ContributionType.VolunteerHours).ContributionTypeId;

        var zero = () => Rules.CreateAsync(new CreateRecognitionRuleCommand(typeId, "Giờ làm", 0, null, Jan1, null, null));
        (await zero.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("RECOGNITION_RULE_POINTS_INVALID");

        var period = () => Rules.CreateAsync(new CreateRecognitionRuleCommand(typeId, "Giờ làm", 5, null, Jan1, Jan1.AddDays(-1), null));
        (await period.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("RECOGNITION_RULE_PERIOD_INVALID");

        var noType = () => Rules.CreateAsync(new CreateRecognitionRuleCommand(null, "Giờ làm", 5, null, Jan1, null, null));
        (await noType.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("CONTRIBUTION_TYPE_REQUIRED");

        _store.Type(ContributionType.VolunteerHours).Update(null, null, ContributionTypeStatus.Inactive);
        var inactive = () => Rules.CreateAsync(new CreateRecognitionRuleCommand(typeId, "Giờ làm", 5, null, Jan1, null, null));
        (await inactive.Should().ThrowAsync<DomainRuleException>()).Which.Code.Should().Be("CONTRIBUTION_TYPE_INACTIVE");
    }

    [Fact]
    public void Deactivation_ends_the_version_and_needs_a_reason()
    {
        var rule = RecognitionRule.Create(Guid.NewGuid(), "Mua hàng", 10, null, Jan1, null);

        FluentActions.Invoking(() => rule.Deactivate(" ", Jan1.AddDays(5)))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("RECOGNITION_RULE_REASON_REQUIRED");

        rule.Deactivate("Đổi chính sách", Jan1.AddDays(5));

        rule.Status.Should().Be(RecognitionRuleStatus.Inactive);
        rule.EffectiveTo.Should().Be(Jan1.AddDays(5));
        rule.AppliesOn(Jan1.AddDays(1)).Should().BeFalse();
        FluentActions.Invoking(() => rule.Deactivate("lại", Jan1.AddDays(6)))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("RECOGNITION_RULE_INACTIVE");
        FluentActions.Invoking(() => rule.NextVersion(5, null, Jan1.AddDays(9), null))
            .Should().Throw<DomainRuleException>().Which.Code.Should().Be("RECOGNITION_RULE_INACTIVE");
    }

    [Fact]
    public void Deactivating_a_future_version_keeps_a_valid_period()
    {
        var rule = RecognitionRule.Create(Guid.NewGuid(), "Mua hàng", 10, null, Jan1.AddDays(30), null);

        rule.Deactivate("Không dùng", Jan1);

        rule.EffectiveTo.Should().Be(rule.EffectiveFrom);
        rule.AppliesOn(rule.EffectiveFrom).Should().BeFalse();
    }

    [Fact]
    public async Task Rules_list_by_name_then_newest_version_with_filters()
    {
        var task = _store.Type(ContributionType.TaskCompletion).ContributionTypeId;
        var money = _store.Type(ContributionType.MonetarySupport).ContributionTypeId;
        var v1 = await Rules.CreateAsync(new CreateRecognitionRuleCommand(task, "B task", 10, null, Jan1, null, null));
        await Rules.CreateAsync(new CreateRecognitionRuleCommand(null, null, 12, null, Jan1.AddDays(3), null, v1.Id));
        await Rules.CreateAsync(new CreateRecognitionRuleCommand(money, "A tiền", 5, null, Jan1, null, null));

        var all = await Rules.ListAsync(null, null);
        all.Select(rule => (rule.Name, rule.RuleVersion)).Should().Equal(("A tiền", 1), ("B task", 2), ("B task", 1));
        (await Rules.ListAsync(null, task)).Should().HaveCount(2);
    }
}
