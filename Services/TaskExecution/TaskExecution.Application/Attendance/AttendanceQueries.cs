using BuildingBlocks.Security;
using TaskExecution.Application.Scopes;
using TaskExecution.Domain;
using TaskExecution.Domain.Attendance;

namespace TaskExecution.Application.Attendance;

public sealed record AttendanceFilter(Guid? SeasonId,Guid? WorkAreaId,AttendanceStatus? Status,DateTimeOffset? From,
    DateTimeOffset? To,int Page,int Size,bool Descending);
public sealed record WorkingHourSummary(Guid VolunteerId,int ApprovedMinutes,decimal ApprovedHours);

public interface IAttendanceReadStore
{
    Task<(IReadOnlyList<AttendanceSession> Items,long Total)> PersonalAsync(Guid volunteerId,Guid parishId,AttendanceFilter filter,CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkingHourSummary>> HoursAsync(Guid areaId,DateTimeOffset? from,DateTimeOffset? to,CancellationToken cancellationToken);
}

public sealed class AttendanceQueries(IAttendanceReadStore store,TaskScopeAccess scopes,ICurrentUser user)
{
    public async Task<(IReadOnlyList<AttendanceDto> Items,long Total)> PersonalAsync(AttendanceFilter filter,CancellationToken ct=default)
    {
        ValidateRange(filter.From,filter.To);
        if (user.Role!=GrottoWorksRoles.Volunteer || !user.ParishId.HasValue)
            throw DomainRuleException.Forbidden("ATTENDANCE_ACCESS_DENIED","A volunteer parish is required.");
        var result=await store.PersonalAsync(user.UserId,user.ParishId.Value,filter,ct);
        return (result.Items.Select(AttendanceDto.From).ToList(),result.Total);
    }

    public async Task<IReadOnlyList<WorkingHourSummary>> HoursAsync(Guid areaId,DateTimeOffset? from,DateTimeOffset? to,CancellationToken ct=default)
    {
        ValidateRange(from,to);
        if (user.Role==GrottoWorksRoles.Leader) await scopes.GetWorkAreaForLeaderReadAsync(areaId,ct);
        else if (user.Role==GrottoWorksRoles.Parish) await scopes.GetWorkAreaForReadAsync(areaId,ct);
        else throw DomainRuleException.Forbidden("ATTENDANCE_ACCESS_DENIED","Parish or assigned leader access is required.");
        return await store.HoursAsync(areaId,from,to,ct);
    }

    public static void ValidateRange(DateTimeOffset? from,DateTimeOffset? to)
    {
        if (from.HasValue && to.HasValue && from>=to)
            throw new DomainRuleException("INVALID_DATE_RANGE","The end must be after the start.",DomainErrorKind.BadRequest);
    }
}
