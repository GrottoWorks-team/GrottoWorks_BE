using System.Text.RegularExpressions;

namespace Reporting.Domain.Recognition;

/// <summary><c>contribution_type.contribution_status</c>.</summary>
public enum ContributionTypeStatus
{
    Active,
    Inactive
}

/// <summary>
/// <c>contribution_type</c> (ERD v3.6.2, F-RPT-01): global catalogue managed by ADMIN. The system codes below are seeded
/// because the event consumers map each confirmed business event to one of them (ERD note: MONEY → MONETARY_SUPPORT,
/// MATERIAL → MATERIAL_DONATION). The code never changes; an inactive type still records contributions (they are facts,
/// BR-54) but earns no new points.
/// </summary>
public sealed partial class ContributionType
{
    public const int MaxCodeLength = 50;
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 2000;

    public const string TaskCompletion = "TASK_COMPLETION";
    public const string VolunteerHours = "VOLUNTEER_HOURS";
    public const string PurchaseAssistance = "PURCHASE_ASSISTANCE";
    public const string MonetarySupport = "MONETARY_SUPPORT";
    public const string MaterialDonation = "MATERIAL_DONATION";
    public const string SupportAssistance = "SUPPORT_ASSISTANCE";

    /// <summary>Fixed ids of the seeded system types (stable across databases).</summary>
    public static readonly IReadOnlyList<(Guid Id, string Code, string Name, string Description)> SystemTypes =
    [
        (new Guid("0f3c2a10-0000-4000-8000-000000000001"), TaskCompletion, "Hoàn thành task", "Task được Leader duyệt hoàn thành."),
        (new Guid("0f3c2a10-0000-4000-8000-000000000002"), VolunteerHours, "Giờ làm được duyệt", "Giờ làm đã được Leader duyệt (BR-21)."),
        (new Guid("0f3c2a10-0000-4000-8000-000000000003"), PurchaseAssistance, "Hỗ trợ mua sắm", "Purchase đã được xác nhận giao."),
        (new Guid("0f3c2a10-0000-4000-8000-000000000004"), MonetarySupport, "Đóng góp tiền", "Donation MONEY đã thực nhận."),
        (new Guid("0f3c2a10-0000-4000-8000-000000000005"), MaterialDonation, "Đóng góp vật tư", "Donation MATERIAL đã thực nhận."),
        (new Guid("0f3c2a10-0000-4000-8000-000000000006"), SupportAssistance, "Hỗ trợ community khác", "Thành viên hỗ trợ đã hoàn thành công việc.")
    ];

    private ContributionType()
    {
    }

    public Guid ContributionTypeId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public ContributionTypeStatus Status { get; private set; }

    public bool IsSystem => SystemTypes.Any(type => type.Code == Code);

    public static ContributionType Create(string? code, string? name, string? description)
    {
        var normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length is 0 or > MaxCodeLength || !CodePattern().IsMatch(normalized))
        {
            throw DomainRuleException.Validation(
                "CONTRIBUTION_TYPE_CODE_INVALID",
                $"Code is required, at most {MaxCodeLength} characters of A–Z, 0–9 and _.");
        }

        var type = new ContributionType { ContributionTypeId = Guid.NewGuid(), Code = normalized, Status = ContributionTypeStatus.Active };
        type.Update(name, description, null);
        return type;
    }

    /// <summary>Rebuilds a seeded system type (used by the EF seed).</summary>
    public static ContributionType System(Guid id, string code, string name, string description) =>
        new() { ContributionTypeId = id, Code = code, Name = name, Description = description, Status = ContributionTypeStatus.Active };

    public void Update(string? name, string? description, ContributionTypeStatus? status)
    {
        if (name is not null || Name.Length == 0)
        {
            Name = Text.Required(name, MaxNameLength, "CONTRIBUTION_TYPE_NAME_INVALID", "Name");
        }

        if (description is not null)
        {
            Description = Text.Optional(description, MaxDescriptionLength, "Description");
        }

        if (status is { } value)
        {
            Status = value;
        }
    }

    [GeneratedRegex("^[A-Z0-9_]+$")]
    private static partial Regex CodePattern();
}
