using System.Text.Json;
using System.Text.Json.Serialization;
using Reporting.Application.Recognition;
using Reporting.Domain;
using Reporting.Domain.Inbox;
using Reporting.Domain.Recognition;
using Resource.Contracts.Events;

namespace Reporting.Application.Ingestion;

/// <summary>One integration event as delivered (envelope of BuildingBlocks README §2 + the broker message id).</summary>
public sealed record IncomingEvent(Guid EventId, string EventType, int Version, DateTimeOffset? OccurredAt, JsonElement Payload);

public enum IngestionOutcome
{
    Applied,
    Duplicate,
    Ignored
}

public sealed record IngestionResult(Guid EventId, string EventType, IngestionOutcome Outcome, int ContributionsRecorded, int PointsAwarded);

public interface IInboxStore
{
    Task<bool> ContainsAsync(Guid eventId, CancellationToken cancellationToken = default);

    void Add(ProcessedEvent processed);
}

/// <summary>
/// F-RPT-02: applies one confirmed business event exactly once (inbox in the same transaction). The MassTransit consumers
/// will call this handler; until the outbox exists the producers (or a seed script) post to <c>POST /internal/events</c>.
/// Events not used by Reporting are acknowledged as IGNORED.
/// </summary>
public sealed class IntegrationEventHandler(IInboxStore inbox, ContributionRecorder recorder)
{
    /// <summary>Raised by the store when the same event id was committed concurrently.</summary>
    public const string AlreadyProcessed = "EVENT_ALREADY_PROCESSED";

    /// <summary>Raised by the store when another event inserted the same contribution / point first (409, redeliver).</summary>
    public const string ConcurrentUpdate = "EVENT_CONCURRENT_UPDATE";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) }
    };

    public async Task<IngestionResult> HandleAsync(IncomingEvent incoming, CancellationToken cancellationToken = default)
    {
        if (await inbox.ContainsAsync(incoming.EventId, cancellationToken))
        {
            return new IngestionResult(incoming.EventId, incoming.EventType, IngestionOutcome.Duplicate, 0, 0);
        }

        var drafts = incoming.EventType switch
        {
            TaskCompletionReviewed.EventType => FromTaskReview(Read<TaskCompletionReviewed>(incoming)),
            AttendanceHoursApproved.EventType => FromAttendance(Read<AttendanceHoursApproved>(incoming)),
            PurchaseDeliveryConfirmed.EventType => FromPurchase(Read<PurchaseDeliveryConfirmed>(incoming)),
            ContributionReceiptConfirmed.EventType => FromReceipt(Read<ContributionReceiptConfirmed>(incoming)),
            ContributionDonationAdjusted.EventType => FromAdjustment(Read<ContributionDonationAdjusted>(incoming)),
            SupportMemberCompleted.EventType => FromSupport(Read<SupportMemberCompleted>(incoming)),
            _ => null
        };

        var now = DateTimeOffset.UtcNow;
        if (drafts is null)
        {
            inbox.Add(ProcessedEvent.Record(incoming.EventId, incoming.EventType, nameof(IngestionOutcome.Ignored), now));
            return await SaveAsync(cancellationToken)
                ? new IngestionResult(incoming.EventId, incoming.EventType, IngestionOutcome.Ignored, 0, 0)
                : new IngestionResult(incoming.EventId, incoming.EventType, IngestionOutcome.Duplicate, 0, 0);
        }

        var (contributions, points) = await recorder.RecordAsync(drafts, now, cancellationToken);
        inbox.Add(ProcessedEvent.Record(incoming.EventId, incoming.EventType, nameof(IngestionOutcome.Applied), now));
        try
        {
            if (!await SaveAsync(cancellationToken))
            {
                return new IngestionResult(incoming.EventId, incoming.EventType, IngestionOutcome.Duplicate, 0, 0);
            }
        }
        catch (DomainRuleException exception) when (exception.Code == ConcurrentUpdate)
        {
            // The same event may have committed first, its contribution row being what collided.
            if (await inbox.ContainsAsync(incoming.EventId, cancellationToken))
            {
                return new IngestionResult(incoming.EventId, incoming.EventType, IngestionOutcome.Duplicate, 0, 0);
            }

            throw;
        }

        await recorder.PublishAsync(points, cancellationToken);
        return new IngestionResult(incoming.EventId, incoming.EventType, IngestionOutcome.Applied, contributions, points.Count);
    }

    /// <returns>false when the same event was committed concurrently (nothing of this attempt is kept).</returns>
    private async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await recorder.SaveAsync(cancellationToken);
            return true;
        }
        catch (DomainRuleException exception) when (exception.Code == AlreadyProcessed)
        {
            return false;
        }
    }

    private static TPayload Read<TPayload>(IncomingEvent incoming)
    {
        try
        {
            return incoming.Payload.Deserialize<TPayload>(JsonOptions)
                ?? throw new JsonException("Empty payload.");
        }
        catch (JsonException exception)
        {
            throw DomainRuleException.Validation("EVENT_PAYLOAD_INVALID", $"Payload of {incoming.EventType} is invalid: {exception.Message}");
        }
    }

    private static List<ContributionDraft> FromTaskReview(TaskCompletionReviewed review)
    {
        // PURCHASE tasks are counted from purchase.delivery.confirmed; only an approval counts (BR-54).
        if (!string.Equals(review.Decision, TaskCompletionReviewed.Approved, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(review.TaskType, "PURCHASE", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return review.VolunteerUserIds.Distinct()
            .Select(userId => ContributionDraft.ForUser(ContributionType.TaskCompletion, userId, review.SeasonId, review.ParishId, review.WorkAreaId,
                "TASK", review.TaskId, $"Hoàn thành task {review.TaskName}".Trim(), review.ReviewedAt))
            .ToList();
    }

    private static List<ContributionDraft> FromAttendance(AttendanceHoursApproved attendance) =>
    [
        ContributionDraft.ForUser(ContributionType.VolunteerHours, attendance.VolunteerUserId, attendance.SeasonId, attendance.ParishId,
            attendance.WorkAreaId, "ATTENDANCE", attendance.AttendanceId, $"{attendance.ApprovedHours:0.##} giờ làm được duyệt", attendance.ApprovedAt)
    ];

    private static List<ContributionDraft> FromPurchase(PurchaseDeliveryConfirmed purchase) =>
    [
        ContributionDraft.ForUser(ContributionType.PurchaseAssistance, purchase.BuyerUserId, purchase.SeasonId, purchase.ParishId,
            purchase.WorkAreaId, "PURCHASE", purchase.PurchaseId, $"Mua hàng {purchase.TotalCost:#,0} {purchase.Currency}", purchase.ConfirmedAt)
    ];

    private static List<ContributionDraft> FromReceipt(ContributionReceiptConfirmed receipt) =>
    [
        ContributionDraft.ForDonation(DonationTypeCode(receipt.DonationType), receipt.DonationId, receipt.DonorUserId, receipt.SeasonId,
            receipt.ParishId, receipt.WorkAreaId,
            receipt.ReceivedAmount is { } amount
                ? $"Đóng góp {amount:#,0} {receipt.Currency}"
                : $"Đóng góp vật tư ({receipt.Items.Count} dòng, {receipt.Items.Sum(item => item.UsableQuantity):0.##} dùng được)",
            receipt.ReceivedAt)
    ];

    private static List<ContributionDraft> FromAdjustment(ContributionDonationAdjusted adjusted) =>
    [
        ContributionDraft.ForDonation(DonationTypeCode(adjusted.DonationType), adjusted.DonationId, adjusted.DonorUserId, adjusted.SeasonId,
            adjusted.ParishId, adjusted.WorkAreaId, $"Đã điều chỉnh: {adjusted.Reason}", adjusted.AdjustedAt)
    ];

    private static List<ContributionDraft> FromSupport(SupportMemberCompleted support) =>
    [
        ContributionDraft.ForUser(ContributionType.SupportAssistance, support.VolunteerUserId, support.SeasonId, support.ParishId,
            support.TargetWorkAreaId, "SUPPORT_RESPONSE_MEMBER", support.SupportResponseMemberId, "Hỗ trợ community khác", support.CompletedAt)
    ];

    /// <summary>ERD note: MONEY → MONETARY_SUPPORT, MATERIAL → MATERIAL_DONATION.</summary>
    private static string DonationTypeCode(string donationType) =>
        donationType.ToUpperInvariant() switch
        {
            "MONEY" => ContributionType.MonetarySupport,
            "MATERIAL" => ContributionType.MaterialDonation,
            _ => throw DomainRuleException.Validation("EVENT_PAYLOAD_INVALID", $"Unknown donation type '{donationType}'.")
        };
}
