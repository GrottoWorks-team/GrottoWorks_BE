using TaskExecution.Domain;
using TaskExecution.Domain.Scopes;

namespace TaskExecution.Application.Tasks;

internal static class TaskScheduleRules
{
    public static void EnsureFitsScope(
        DateTimeOffset? startAt,
        DateTimeOffset? deadline,
        WorkAreaScope workArea,
        SeasonScope season)
    {
        EnsureDateInsideSeason(startAt, season);
        EnsureDateInsideSeason(deadline, season);

        if (startAt.HasValue && workArea.StartDate.HasValue &&
            DateOnly.FromDateTime(startAt.Value.UtcDateTime) < workArea.StartDate.Value)
        {
            throw DomainRuleException.Validation(
                "TASK_DATE_OUT_OF_WORK_AREA",
                "Task start time is before the work area starts.");
        }

        if (deadline.HasValue && workArea.Deadline.HasValue &&
            DateOnly.FromDateTime(deadline.Value.UtcDateTime) > workArea.Deadline.Value)
        {
            throw DomainRuleException.Validation(
                "TASK_DATE_OUT_OF_WORK_AREA",
                "Task deadline is after the work area deadline.");
        }
    }

    private static void EnsureDateInsideSeason(DateTimeOffset? value, SeasonScope season)
    {
        if (!value.HasValue)
        {
            return;
        }

        var date = DateOnly.FromDateTime(value.Value.UtcDateTime);
        if (date < season.StartDate || date > season.EndDate)
        {
            throw DomainRuleException.Validation(
                "TASK_DATE_OUT_OF_SEASON",
                "Task schedule must be within the owning season.");
        }
    }
}
