using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TaskExecution.Application.Events;
using TaskExecution.Application.Users;
using TaskExecution.Domain;

namespace TaskExecution.Infrastructure.Identity;

public sealed class IdentityUserLookup(HttpClient client, IConfiguration configuration, ITaskCorrelationContext correlation)
    : IIdentityUserLookup, IIdentityUserBatchLookup
{
    public async Task<IdentityUser?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await FindManyAsync([userId],cancellationToken)).GetValueOrDefault(userId);

    public async Task<IReadOnlyDictionary<Guid,IdentityUser>> FindManyAsync(IReadOnlyCollection<Guid> userIds,CancellationToken cancellationToken=default)
    {
        var result=new Dictionary<Guid,IdentityUser>();
        foreach(var batch in userIds.Distinct().Chunk(100))
            foreach(var user in await GetBatchAsync(batch,cancellationToken)) result.Add(user.UserId,user);
        return result;
    }

    private async Task<IReadOnlyList<IdentityUser>> GetBatchAsync(Guid[] userIds,CancellationToken cancellationToken)
    {
        var key = configuration["InternalApi:ApiKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32) throw Unavailable();
        try
        {
            var query=string.Join("&",userIds.Select(id=>$"ids={id:D}"));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"internal/users?{query}");
            request.Headers.Add("X-Internal-Api-Key", key);
            request.Headers.Add("X-Correlation-Id", correlation.CorrelationId);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw Unavailable();
            var envelope = await response.Content.ReadFromJsonAsync<UserEnvelope>(cancellationToken);
            if (envelope?.Data == null || envelope.Data.Count > userIds.Length || envelope.Data.Any(row => row is null || !userIds.Contains(row.UserId) ||
                string.IsNullOrWhiteSpace(row.Role) || string.IsNullOrWhiteSpace(row.Status) || !ValidSkills(row)))
                throw Unavailable();
            if (envelope.Data.Select(row=>row.UserId).Distinct().Count()!=envelope.Data.Count) throw Unavailable();
            return envelope.Data;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException or
            Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException)
        { throw Unavailable(); }
    }

    private static DomainRuleException Unavailable() => new("IDENTITY_SERVICE_UNAVAILABLE",
        "Identity user validation is temporarily unavailable.", DomainErrorKind.ServiceUnavailable);
    private sealed record UserEnvelope(IReadOnlyList<IdentityUser>? Data);

    internal static bool ValidSkills(IdentityUser user)
    {
        if (user.SkillIds is null || user.Skills is null || user.Skills.Any(skill => skill is null || skill.SkillId == Guid.Empty ||
            skill.SkillLevel is not (null or "BASIC" or "INTERMEDIATE" or "ADVANCED" or "EXPERT"))) return false;
        if (user.SkillIds.Distinct().Count() != user.SkillIds.Count || user.Skills.Select(skill => skill.SkillId).Distinct().Count() != user.Skills.Count)
            return false;
        return user.SkillIds.Order().SequenceEqual(user.Skills.Select(skill => skill.SkillId).Order());
    }
}
