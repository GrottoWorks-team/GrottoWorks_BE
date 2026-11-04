using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NotificationAudit.Application.Events;

namespace NotificationAudit.Infrastructure.Deadlines;

public sealed class NotificationAudienceResolver(HttpClient client,IHttpClientFactory clients,IConfiguration configuration) : INotificationAudienceResolver
{
    public async Task<IReadOnlyList<Guid>?> ResolveAsync(IncomingIntegrationEvent message,CancellationToken ct)
    {
        try { return await ResolveCoreAsync(message,ct); }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested) { return null; }
        catch(Exception exception) when(exception is HttpRequestException or JsonException or
            Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException)
        {
            // Retain the safe source envelope. The context replay worker will retry dependency outages.
            return null;
        }
    }

    private async Task<IReadOnlyList<Guid>?> ResolveCoreAsync(IncomingIntegrationEvent message,CancellationToken ct)
    {
        var areas=await GetCompleteAudienceAsync(message,ct);
        if(areas is null)return null;
        if(message.EventType.StartsWith("planning.season.",StringComparison.Ordinal))
            return areas.SelectMany(row=>new[] {row.LeaderUserId,row.MaterialOfficerUserId}).Where(id=>id.HasValue).Select(id=>id!.Value).Distinct().Order().ToArray();
        if(message.EventType!="support.request.published")return null;
        var type=message.Payload.GetProperty("supportType").GetString();
        var recipients=new HashSet<Guid>();
        if(type is "MATERIAL" or "MIXED")
            foreach(var area in areas.Where(row=>row.MaterialOfficerUserId.HasValue && row.WorkAreaId!=message.Payload.GetProperty("workAreaId").GetGuid()))
                recipients.Add(area.MaterialOfficerUserId!.Value);
        if(type is "STAFFING" or "MIXED")
        {
            var positions=message.Payload.GetProperty("positions").Deserialize<Position[]>(JsonSerializerOptions.Web)
                ?? throw new InvalidNotificationEventException("Missing matching requirements.");
            var key=configuration["InternalApi:ApiKey"];
            if(string.IsNullOrWhiteSpace(key) || key.Length<32)throw new HttpRequestException("Internal audience service is not configured.");
            for(var page=1;page<=100;page++)
            {
                using var request=new HttpRequestMessage(HttpMethod.Get,$"internal/volunteers?parishId={message.ParishId:D}&page={page}&size=100");
                request.Headers.Add("X-Internal-Api-Key",key);request.Headers.Add("X-Correlation-Id",message.CorrelationId);
                using var response=await client.SendAsync(request,ct);response.EnsureSuccessStatusCode();
                var result=await response.Content.ReadFromJsonAsync<DirectoryPage>(ct);
                if(result?.Data is null || result.Page is null || result.Page.TotalPages>100)
                    throw new HttpRequestException("Audience directory returned incomplete pagination.");
                foreach(var candidate in result.Data)
                {
                    if(candidate.ParishId!=message.ParishId || candidate.Role!="VOLUNTEER" || candidate.Status!="ACTIVE" || candidate.UserId==Guid.Empty || candidate.Skills is null)
                        throw new HttpRequestException("Audience directory returned invalid tenant context.");
                    if(positions.Any(position=>position.Skills.All(requirement=>candidate.Skills.Any(skill=>skill.SkillId==requirement.SkillId &&
                        (requirement.RequiredSkillLevel is null || Level(skill.SkillLevel)>=Level(requirement.RequiredSkillLevel))))))recipients.Add(candidate.UserId);
                }
                if(page>=result.Page.TotalPages)break;
            }
        }
        return recipients.Order().ToArray();
    }
    private async Task<IReadOnlyList<Area>?> GetCompleteAudienceAsync(IncomingIntegrationEvent message,CancellationToken ct)
    {
        var key=configuration["InternalApi:ApiKey"];
        if(string.IsNullOrWhiteSpace(key) || key.Length<32)
            throw new HttpRequestException("Internal audience service is not configured.");
        using var request=new HttpRequestMessage(HttpMethod.Get,
            $"internal/notification-audience?parishId={message.ParishId:D}&seasonId={message.SeasonId:D}");
        request.Headers.Add("X-Internal-Api-Key",key);
        request.Headers.Add("X-Correlation-Id",message.CorrelationId);
        using var response=await clients.CreateClient("NotificationPlanningAudience").SendAsync(request,ct);
        if(response.StatusCode==System.Net.HttpStatusCode.NotFound)return null;
        response.EnsureSuccessStatusCode();
        var result=await response.Content.ReadFromJsonAsync<AudienceEnvelope>(ct);
        if(result?.Data is null || result.Data.Any(area=>area.WorkAreaId==Guid.Empty ||
            area.ParishId!=message.ParishId || area.SeasonId!=message.SeasonId) ||
            result.Data.Select(area=>area.WorkAreaId).Distinct().Count()!=result.Data.Length)
            throw new HttpRequestException("Planning audience returned invalid ownership context.");
        // A partial local projection cannot prove that all material officers have been considered.
        if(message.EventType=="support.request.published" &&
            !result.Data.Any(area=>area.WorkAreaId==message.Payload.GetProperty("workAreaId").GetGuid()))
            return null;
        return result.Data;
    }
    private sealed record Area(Guid WorkAreaId,Guid SeasonId,Guid ParishId,Guid? LeaderUserId,Guid? MaterialOfficerUserId);
    private sealed record AudienceEnvelope(Area[]? Data);
    private static int Level(string? value)=>value switch {"BASIC"=>1,"INTERMEDIATE"=>2,"ADVANCED"=>3,"EXPERT"=>4,_=>0};
    private sealed record Position(Skill[] Skills);
    private sealed record Skill(Guid SkillId,string? RequiredSkillLevel);
    private sealed record DeclaredSkill(Guid SkillId,string? SkillLevel);
    private sealed record Candidate(Guid UserId,string Status,string Role,Guid? ParishId,DeclaredSkill[] Skills);
    private sealed record PageInfo(int TotalPages);
    private sealed record DirectoryPage(Candidate[]? Data,PageInfo? Page);
}
