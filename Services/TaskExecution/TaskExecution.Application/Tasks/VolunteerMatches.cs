using TaskExecution.Application.Assignments;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Assignments;

namespace TaskExecution.Application.Tasks;

public sealed record VolunteerDirectoryPage(IReadOnlyList<IdentityUser> Items, long TotalItems);

public interface IVolunteerDirectory
{
    Task<VolunteerDirectoryPage> GetPageAsync(Guid parishId, Guid? communityId, int page, int size, CancellationToken cancellationToken);
}

public interface IVolunteerScheduleReader
{
    Task<IReadOnlySet<Guid>> GetConflictsAsync(IReadOnlyCollection<Guid> volunteerIds, Guid taskId,
        DateTimeOffset? startAt, DateTimeOffset deadline, CancellationToken cancellationToken);
}

public sealed record VolunteerMatchDto(Guid VolunteerId, string DisplayName, Guid? CommunityId,
    bool SameCommunity, bool? AvailabilityMatched, bool HasScheduleConflict, IReadOnlyList<string> Warnings);

public sealed class GetVolunteerMatchesHandler(ITaskRepository tasks, TaskScopeAccess access,
    IAssignmentStore assignments, IVolunteerDirectory directory, IVolunteerScheduleReader schedules)
{
    public async Task<IReadOnlyList<VolunteerMatchDto>> HandleAsync(Guid taskId, Guid? communityId, int limit,
        CancellationToken cancellationToken)
    {
        if (communityId == Guid.Empty || limit is < 1 or > 100)
            throw new DomainRuleException("INVALID_MATCH_QUERY", "Limit must be 1–100 and community ID must not be empty.", DomainErrorKind.BadRequest);
        var task = await tasks.GetByIdAsync(taskId, cancellationToken)
            ?? throw DomainRuleException.NotFound("TASK_NOT_FOUND", "The task does not exist.");
        var area = await access.GetWorkAreaForLeaderReadAsync(task.WorkAreaId, cancellationToken);
        var current = await assignments.ListAsync(taskId, cancellationToken);
        var excluded = current.Where(row => row.Status is AssignmentStatus.Pending or AssignmentStatus.Accepted or
            AssignmentStatus.InProgress or AssignmentStatus.Completed).Select(row => row.VolunteerId).ToHashSet();

        var candidates = new Dictionary<Guid, IdentityUser>();
        const int pageSize = 100;
        const int maximumCandidates = 10000;
        for (var page = 1; ; page++)
        {
            var batch = await directory.GetPageAsync(area.ParishId, communityId, page, pageSize, cancellationToken);
            if (batch.TotalItems > maximumCandidates)
                throw new DomainRuleException("IDENTITY_SERVICE_UNAVAILABLE", "The volunteer directory exceeds the supported matching batch.", DomainErrorKind.ServiceUnavailable);
            foreach (var volunteer in batch.Items)
            {
                if (volunteer.Status != "ACTIVE" || volunteer.Role != "VOLUNTEER" || volunteer.ParishId != area.ParishId ||
                    excluded.Contains(volunteer.UserId) || (communityId.HasValue && volunteer.CommunityId != communityId)) continue;
                if (task.Skills.All(skill => VolunteerSkills.Meets(volunteer, skill.SkillId, skill.RequiredSkillLevel)))
                    candidates[volunteer.UserId] = volunteer;
            }
            if ((long)page * pageSize >= batch.TotalItems) break;
            if (batch.Items.Count == 0)
                throw new DomainRuleException("IDENTITY_SERVICE_UNAVAILABLE", "The volunteer directory returned an incomplete page.", DomainErrorKind.ServiceUnavailable);
        }

        var conflicts = await schedules.GetConflictsAsync(candidates.Keys.ToArray(), task.Id, task.StartAt, task.Deadline, cancellationToken);
        var results = new List<VolunteerMatchDto>(candidates.Count);
        foreach (var volunteer in candidates.Values)
        {
            var available = AvailabilityCovers(volunteer.Availability, task.StartAt, task.Deadline);
            var conflict = conflicts.Contains(volunteer.UserId);
            var warnings = new List<string>();
            if (available is null) warnings.Add("AVAILABILITY_UNDETERMINED");
            else if (!available.Value) warnings.Add("OUTSIDE_DECLARED_AVAILABILITY");
            if (conflict) warnings.Add("SCHEDULE_CONFLICT");
            if (!task.StartAt.HasValue) warnings.Add("SCHEDULE_UNDETERMINED");
            results.Add(new(volunteer.UserId, volunteer.DisplayName, volunteer.CommunityId,
                area.CommunityId.HasValue && area.CommunityId == volunteer.CommunityId, available, conflict, warnings));
        }
        // Matching is advisory. Schedule/availability warnings never prohibit assignment.
        return results.OrderByDescending(row => row.AvailabilityMatched == true)
            .ThenBy(row => row.HasScheduleConflict).ThenByDescending(row => row.SameCommunity)
            .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.VolunteerId)
            .Take(limit).ToArray();
    }

    public static bool? AvailabilityCovers(IReadOnlyList<IdentityAvailability>? availability,
        DateTimeOffset? startAt, DateTimeOffset deadline)
    {
        if (!startAt.HasValue || availability is null || availability.Count == 0) return null;
        var coveredUntil = startAt.Value;
        foreach (var slot in availability.OrderBy(slot => slot.AvailableFrom))
        {
            if (slot.AvailableTo <= coveredUntil) continue;
            if (slot.AvailableFrom > coveredUntil) return false;
            coveredUntil = slot.AvailableTo;
            if (coveredUntil >= deadline) return true;
        }
        return false;
    }
}
