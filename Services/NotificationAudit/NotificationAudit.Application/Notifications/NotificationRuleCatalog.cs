using System.Collections.ObjectModel;

namespace NotificationAudit.Application.Notifications;

public static class NotificationRuleCatalog
{
    // IDs are part of the admin UI contract. Keep existing IDs when correcting an event name.
    public static IReadOnlyDictionary<Guid, string> All { get; } = new ReadOnlyDictionary<Guid, string>(new Dictionary<Guid, string>
    {
        [Guid.Parse("71000000-0000-0000-0000-000000000001")] = "task.assignment.created",
        [Guid.Parse("71000000-0000-0000-0000-000000000002")] = "task.assignment.responded",
        [Guid.Parse("71000000-0000-0000-0000-000000000003")] = "task.status.changed",
        [Guid.Parse("71000000-0000-0000-0000-000000000004")] = "attendance.request.submitted",
        [Guid.Parse("71000000-0000-0000-0000-000000000005")] = "attendance.request.reviewed",
        [Guid.Parse("71000000-0000-0000-0000-000000000006")] = "attendance.hours.approved",
        [Guid.Parse("71000000-0000-0000-0000-000000000007")] = "attendance.session.open-too-long",
        [Guid.Parse("71000000-0000-0000-0000-000000000008")] = "planning.season.activated",
        [Guid.Parse("71000000-0000-0000-0000-000000000009")] = "planning.season.completed",
        [Guid.Parse("71000000-0000-0000-0000-000000000010")] = "planning.season.archived",
        [Guid.Parse("71000000-0000-0000-0000-000000000011")] = "purchase.request.decided",
        [Guid.Parse("71000000-0000-0000-0000-000000000012")] = "readiness.area.ready",
        [Guid.Parse("71000000-0000-0000-0000-000000000013")] = "purchase.request.submitted",
        [Guid.Parse("71000000-0000-0000-0000-000000000014")] = "purchase.delivery.confirmed",
        [Guid.Parse("71000000-0000-0000-0000-000000000015")] = "material-report.submitted",
        [Guid.Parse("71000000-0000-0000-0000-000000000016")] = "material-report.verified",
        [Guid.Parse("71000000-0000-0000-0000-000000000017")] = "contribution.pledged",
        [Guid.Parse("71000000-0000-0000-0000-000000000018")] = "contribution.receipt.confirmed",
        [Guid.Parse("71000000-0000-0000-0000-000000000019")] = "contribution.donation.adjusted",
        [Guid.Parse("71000000-0000-0000-0000-000000000020")] = "loan.offer.submitted",
        [Guid.Parse("71000000-0000-0000-0000-000000000021")] = "support.transfer.confirmed",
        [Guid.Parse("71000000-0000-0000-0000-000000000022")] = "recognition.entry.created",
        [Guid.Parse("71000000-0000-0000-0000-000000000023")] = "readiness.item.failed",
        [Guid.Parse("71000000-0000-0000-0000-000000000024")] = "readiness.area.reopened",
        [Guid.Parse("71000000-0000-0000-0000-000000000025")] = "planning.work-area.assigned",
        [Guid.Parse("71000000-0000-0000-0000-000000000026")] = "support.member.reviewed",
        [Guid.Parse("71000000-0000-0000-0000-000000000027")] = "support.request.published",
        [Guid.Parse("71000000-0000-0000-0000-000000000028")] = "support.material.approved",
        [Guid.Parse("71000000-0000-0000-0000-000000000029")] = "support.material.reviewed",
        [Guid.Parse("71000000-0000-0000-0000-000000000030")] = "task.deadline.reminder",
        [Guid.Parse("71000000-0000-0000-0000-000000000031")] = "task.deadline.overdue",
        [Guid.Parse("71000000-0000-0000-0000-000000000032")] = "task.progress.submitted",
        [Guid.Parse("71000000-0000-0000-0000-000000000033")] = "task.completion.reviewed",
        [Guid.Parse("71000000-0000-0000-0000-000000000034")] = "attendance.session.closed-by-leader"
    });
}
