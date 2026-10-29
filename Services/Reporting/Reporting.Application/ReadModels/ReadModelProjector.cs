using System.Text.Json;
using Readiness.Contracts.Events;
using Reporting.Application.Ingestion;
using Reporting.Domain.ReadModels;
using Resource.Contracts.Events;

namespace Reporting.Application.ReadModels;

public interface IReadModelStore
{
    Task<MaterialLine?> GetMaterialLineAsync(Guid workAreaId, Guid materialId, CancellationToken cancellationToken = default);

    Task<PurchaseRequestView?> GetPurchaseRequestAsync(Guid purchaseRequestId, CancellationToken cancellationToken = default);

    Task<DonationView?> GetDonationAsync(Guid donationId, CancellationToken cancellationToken = default);

    Task<AttendanceView?> GetAttendanceAsync(Guid attendanceId, CancellationToken cancellationToken = default);

    Task<ReadinessAreaView?> GetReadinessAreaAsync(Guid workAreaId, CancellationToken cancellationToken = default);

    void Add(MaterialLine line);

    void Add(PurchaseRequestView request);

    void Add(DonationView donation);

    void Add(AttendanceView attendance);

    void Add(ReadinessAreaView area);

    void Add(ReadinessHistoryEntry entry);

    Task<IReadOnlyList<MaterialLine>> ListMaterialLinesAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseRequestView>> ListPurchaseRequestsAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default);

    /// <summary>Donations of the season in the parish (with or without a receiving work area).</summary>
    Task<IReadOnlyList<DonationView>> ListDonationsAsync(Guid parishId, Guid seasonId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttendanceView>> ListAttendanceAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReadinessAreaView>> ListReadinessAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReadinessHistoryEntry>> ListReadinessHistoryAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Domain.Scope.TaskProjection>> ListTasksAsync(IReadOnlyCollection<Guid> workAreaIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// F-RPT-04: keeps the dashboard / report read models up to date from integration events, in the same unit of work as the
/// inbox row (so a redelivered event changes nothing). Returns false for events it does not use.
/// </summary>
public sealed class ReadModelProjector(IReadModelStore store)
{
    public async Task<bool> ApplyAsync(IncomingEvent incoming, Func<Type, object> read, CancellationToken cancellationToken)
    {
        switch (incoming.EventType)
        {
            case MaterialShortageChanged.EventType:
            {
                var e = (MaterialShortageChanged)read(typeof(MaterialShortageChanged));
                var line = await LineAsync(e.WorkAreaId, e.MaterialId, e.SeasonId, e.ParishId, cancellationToken);
                line.SetBalance(e.RequiredQuantity, e.ConfirmedAvailable, e.Shortage, e.MaterialCode, e.MaterialName, e.Unit, e.OccurredAt);
                return true;
            }

            case ContributionPledged.EventType:
            {
                var e = (ContributionPledged)read(typeof(ContributionPledged));
                var donation = await DonationAsync(e.DonationId, e.DonorId, e.DonorUserId, e.SeasonId, e.ParishId, e.WorkAreaId, e.DonationType, cancellationToken);
                donation.Pledge(e.PledgedAmount, e.Items.Sum(item => item.PledgedQuantity), e.PledgedAt);
                if (e.WorkAreaId is { } area)
                {
                    foreach (var item in e.Items)
                    {
                        (await LineAsync(area, item.MaterialId, e.SeasonId, e.ParishId, cancellationToken)).AddPledged(item.PledgedQuantity, e.PledgedAt);
                    }
                }

                return true;
            }

            case ContributionReceiptConfirmed.EventType:
            {
                var e = (ContributionReceiptConfirmed)read(typeof(ContributionReceiptConfirmed));
                var donation = await DonationAsync(e.DonationId, e.DonorId, e.DonorUserId, e.SeasonId, e.ParishId, e.WorkAreaId, e.DonationType, cancellationToken);
                donation.Receive(e.DonorUserId, e.ReceivedAmount ?? 0, e.Items.Sum(item => item.UsableQuantity), e.Items.Sum(item => item.UnusableQuantity),
                    e.ReceivedAt);
                if (e.WorkAreaId is { } area)
                {
                    foreach (var item in e.Items)
                    {
                        (await LineAsync(area, item.MaterialId, e.SeasonId, e.ParishId, cancellationToken))
                            .AddDonated(item.UsableQuantity, item.UnusableQuantity, e.ReceivedAt);
                    }
                }

                return true;
            }

            case ContributionDonationAdjusted.EventType:
            {
                var e = (ContributionDonationAdjusted)read(typeof(ContributionDonationAdjusted));
                var donation = await DonationAsync(e.DonationId, e.DonorId, e.DonorUserId, e.SeasonId, e.ParishId, e.WorkAreaId, e.DonationType, cancellationToken);
                donation.Adjust(e.AmountDelta, e.Items.Sum(item => item.UsableDelta), e.Items.Sum(item => item.UnusableDelta), e.AdjustedAt);
                if (e.WorkAreaId is { } area)
                {
                    foreach (var item in e.Items)
                    {
                        (await LineAsync(area, item.MaterialId, e.SeasonId, e.ParishId, cancellationToken))
                            .AddDonated(item.UsableDelta, item.UnusableDelta, e.AdjustedAt);
                    }
                }

                return true;
            }

            case PurchaseRequestSubmitted.EventType:
            {
                var e = (PurchaseRequestSubmitted)read(typeof(PurchaseRequestSubmitted));
                (await RequestAsync(e.PurchaseRequestId, e.WorkAreaId, e.SeasonId, e.ParishId, e.RequestedByUserId, cancellationToken))
                    .Submitted(e.EstimatedTotal, e.OccurredAt);
                return true;
            }

            case PurchaseRequestDecided.EventType:
            {
                var e = (PurchaseRequestDecided)read(typeof(PurchaseRequestDecided));
                (await RequestAsync(e.PurchaseRequestId, e.WorkAreaId, e.SeasonId, e.ParishId, e.RequestedByUserId, cancellationToken))
                    .Decided(e.Decision, e.EstimatedTotal, e.DecidedAt);
                return true;
            }

            case PurchaseDeliveryConfirmed.EventType:
            {
                var e = (PurchaseDeliveryConfirmed)read(typeof(PurchaseDeliveryConfirmed));
                (await RequestAsync(e.PurchaseRequestId, e.WorkAreaId, e.SeasonId, e.ParishId, Guid.Empty, cancellationToken))
                    .Delivered(e.TotalCost, e.RequestStatus, e.ConfirmedAt);
                foreach (var item in e.Items)
                {
                    (await LineAsync(e.WorkAreaId, item.MaterialId, e.SeasonId, e.ParishId, cancellationToken))
                        .AddPurchased(item.ReceivedUsableQuantity, item.ReceivedUnusableQuantity, e.ConfirmedAt);
                }

                return true;
            }

            case AttendanceHoursApproved.EventType:
            {
                var e = (AttendanceHoursApproved)read(typeof(AttendanceHoursApproved));
                var attendance = await store.GetAttendanceAsync(e.AttendanceId, cancellationToken);
                if (attendance is null)
                {
                    attendance = AttendanceView.Create(e.AttendanceId, e.VolunteerUserId, e.TaskId, e.WorkAreaId, e.SeasonId, e.ParishId);
                    store.Add(attendance);
                }

                attendance.Approve(e.ApprovedHours, e.ApprovedAt);
                return true;
            }

            case ReadinessItemFailed.EventType:
            {
                var e = (ReadinessItemFailed)read(typeof(ReadinessItemFailed));
                (await AreaAsync(e.WorkAreaId, e.ReadinessChecklistId, e.SeasonId, e.ParishId, cancellationToken)).ItemFailed(e.Mandatory, e.CheckedAt);
                store.Add(ReadinessHistoryEntry.Record(e.WorkAreaId, e.ReadinessChecklistId, e.SeasonId, e.ParishId, "ITEM_FAILED", e.ItemName, e.Mandatory,
                    e.Note, e.CheckedByUserId, e.CheckedAt));
                return true;
            }

            case ReadinessAreaReady.EventType:
            {
                var e = (ReadinessAreaReady)read(typeof(ReadinessAreaReady));
                (await AreaAsync(e.WorkAreaId, e.ReadinessChecklistId, e.SeasonId, e.ParishId, cancellationToken)).Ready(e.ConfirmedAt);
                store.Add(ReadinessHistoryEntry.Record(e.WorkAreaId, e.ReadinessChecklistId, e.SeasonId, e.ParishId, "AREA_READY", null, null, null,
                    e.ConfirmedByUserId, e.ConfirmedAt));
                return true;
            }

            case ReadinessAreaReopened.EventType:
            {
                var e = (ReadinessAreaReopened)read(typeof(ReadinessAreaReopened));
                (await AreaAsync(e.WorkAreaId, e.ReadinessChecklistId, e.SeasonId, e.ParishId, cancellationToken)).Reopened(e.ReopenedAt);
                store.Add(ReadinessHistoryEntry.Record(e.WorkAreaId, e.ReadinessChecklistId, e.SeasonId, e.ParishId, "AREA_REOPENED", null, null, e.Reason,
                    e.ReopenedByUserId, e.ReopenedAt));
                return true;
            }

            default:
                return false;
        }
    }

    private async Task<MaterialLine> LineAsync(Guid workAreaId, Guid materialId, Guid seasonId, Guid parishId, CancellationToken cancellationToken)
    {
        var line = await store.GetMaterialLineAsync(workAreaId, materialId, cancellationToken);
        if (line is null)
        {
            line = MaterialLine.Create(workAreaId, materialId, seasonId, parishId);
            store.Add(line);
        }

        return line;
    }

    private async Task<DonationView> DonationAsync(Guid donationId, Guid donorId, Guid? donorUserId, Guid seasonId, Guid parishId, Guid? workAreaId,
        string type, CancellationToken cancellationToken)
    {
        var donation = await store.GetDonationAsync(donationId, cancellationToken);
        if (donation is null)
        {
            donation = DonationView.Create(donationId, donorId, donorUserId, seasonId, parishId, workAreaId, type);
            store.Add(donation);
        }

        return donation;
    }

    private async Task<PurchaseRequestView> RequestAsync(Guid id, Guid workAreaId, Guid seasonId, Guid parishId, Guid requestedBy,
        CancellationToken cancellationToken)
    {
        var request = await store.GetPurchaseRequestAsync(id, cancellationToken);
        if (request is null)
        {
            request = PurchaseRequestView.Create(id, workAreaId, seasonId, parishId, requestedBy);
            store.Add(request);
        }

        return request;
    }

    private async Task<ReadinessAreaView> AreaAsync(Guid workAreaId, Guid checklistId, Guid seasonId, Guid parishId, CancellationToken cancellationToken)
    {
        var area = await store.GetReadinessAreaAsync(workAreaId, cancellationToken);
        if (area is null)
        {
            area = ReadinessAreaView.Create(workAreaId, checklistId, seasonId, parishId);
            store.Add(area);
        }

        return area;
    }
}
