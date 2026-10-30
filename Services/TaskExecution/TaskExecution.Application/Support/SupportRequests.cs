using BuildingBlocks.Security;
using TaskExecution.Application.Scopes;
using TaskExecution.Application.Users;
using TaskExecution.Domain;
using TaskExecution.Domain.Support;

namespace TaskExecution.Application.Support;

public sealed record CreateSupportCommand(Guid SeasonId,Guid WorkAreaId,SupportType SupportType,string? Description,DateOnly? Deadline,
    IReadOnlyList<SupportPositionInput> Positions,IReadOnlyList<SupportMaterialInput> Materials);
public sealed record UpdateSupportCommand(Guid Id,SupportType? SupportType,bool ChangeDescription,string? Description,
    bool ChangeDeadline,DateOnly? Deadline,bool ChangePositions,IReadOnlyList<SupportPositionInput>? Positions,
    bool ChangeMaterials,IReadOnlyList<SupportMaterialInput>? Materials,int? ExpectedVersion);
public sealed record SupportSkillDto(Guid SkillId,string? RequiredSkillLevel);
public sealed record SupportPositionDto(Guid Id,string Name,int RequiredVolunteerCount,string? Note,IReadOnlyList<SupportSkillDto> Skills);
public sealed record SupportMaterialDto(Guid Id,Guid MaterialId,decimal RequiredQuantity,string? Note);
public sealed record SupportRequestDto(Guid Id,Guid SeasonId,Guid WorkAreaId,Guid RequestedBy,SupportType SupportType,string? Description,
    DateOnly? Deadline,SupportRequestStatus Status,DateTimeOffset CreatedAt,DateTimeOffset? UpdatedAt,int Version,
    IReadOnlyList<SupportPositionDto> Positions,IReadOnlyList<SupportMaterialDto> Materials,IReadOnlyList<object> Responses)
{
    public static SupportRequestDto From(SupportRequest row)=>new(row.Id,row.SeasonId,row.WorkAreaId,row.RequestedBy,row.SupportType,
        row.Description,row.Deadline,row.Status,row.CreatedAt,row.UpdatedAt,row.Version,
        row.Positions.OrderBy(p=>p.Name).ThenBy(p=>p.Id).Select(p=>new SupportPositionDto(p.Id,p.Name,p.RequiredVolunteerCount,p.Note,
            p.Skills.OrderBy(s=>s.SkillId).Select(s=>new SupportSkillDto(s.SkillId,s.RequiredSkillLevel)).ToList())).ToList(),
        row.Materials.OrderBy(m=>m.MaterialId).Select(m=>new SupportMaterialDto(m.Id,m.MaterialId,m.RequiredQuantity,m.Note)).ToList(),[]);
}

public interface ISupportRequestRepository
{
    Task<SupportRequest?> GetAsync(Guid id,bool tracked,CancellationToken cancellationToken);
    Task<(IReadOnlyList<SupportRequest> Items,long Total)> GetPageAsync(Guid? parishId,Guid? seasonId,Guid? workAreaId,
        SupportType? type,SupportRequestStatus? status,int page,int size,bool descending,CancellationToken cancellationToken);
    void Add(SupportRequest request);
    Task SaveAsync(CancellationToken cancellationToken);
}

public sealed partial class SupportRequestService(ISupportRequestRepository repository,TaskScopeAccess scopes,ActiveUserAccess users,ICurrentUser user)
{
    public async Task<SupportRequestDto> UpdateAsync(UpdateSupportCommand command,CancellationToken ct=default)
    {
        var request=await repository.GetAsync(command.Id,true,ct)
            ?? throw DomainRuleException.NotFound("SUPPORT_REQUEST_NOT_FOUND","The support request does not exist.");
        var (area,_)=await scopes.GetWorkAreaForLeaderWriteAsync(request.WorkAreaId,ct);
        await users.EnsureCallerAsync(area.ParishId,ct);
        request.Update(command.SupportType,command.ChangeDescription,command.Description,command.ChangeDeadline,command.Deadline,
            command.ChangePositions,command.Positions,command.ChangeMaterials,command.Materials,command.ExpectedVersion,DateTimeOffset.UtcNow);
        await repository.SaveAsync(ct);
        return SupportRequestDto.From(request);
    }

    public async Task<(IReadOnlyList<SupportRequestDto> Items,long Total)> ListAsync(Guid? seasonId,Guid? workAreaId,
        SupportType? type,SupportRequestStatus? status,int page,int size,bool descending,CancellationToken ct=default)
    {
        Guid? parishId=user.Role==GrottoWorksRoles.Admin ? null : user.ParishId;
        if (user.Role!=GrottoWorksRoles.Admin && !parishId.HasValue)
            throw DomainRuleException.Forbidden("PARISH_ACCESS_DENIED","A parish is required to list support requests.");
        if (workAreaId.HasValue) await scopes.GetWorkAreaForReadAsync(workAreaId.Value,ct);
        var result=await repository.GetPageAsync(parishId,seasonId,workAreaId,type,status,page,size,descending,ct);
        return (result.Items.Select(SupportRequestDto.From).ToList(),result.Total);
    }

    public async Task<SupportRequestDto> CreateAsync(CreateSupportCommand command,CancellationToken ct=default)
    {
        var (area,_)=await scopes.GetWorkAreaForLeaderWriteAsync(command.WorkAreaId,ct);
        await users.EnsureCallerAsync(area.ParishId,ct);
        if (area.SeasonId!=command.SeasonId)
            throw DomainRuleException.Validation("SUPPORT_SEASON_MISMATCH","The work area belongs to another season.");
        var request=SupportRequest.Create(command.SeasonId,command.WorkAreaId,user.UserId,command.SupportType,command.Description,
            command.Deadline,command.Positions,command.Materials,DateTimeOffset.UtcNow);
        repository.Add(request);await repository.SaveAsync(ct);
        return SupportRequestDto.From(request);
    }

    public async Task<SupportRequestDto> GetAsync(Guid id,CancellationToken ct=default)
    {
        var request=await repository.GetAsync(id,false,ct)
            ?? throw DomainRuleException.NotFound("SUPPORT_REQUEST_NOT_FOUND","The support request does not exist.");
        await scopes.GetWorkAreaForReadAsync(request.WorkAreaId,ct);
        return SupportRequestDto.From(request);
    }
}
