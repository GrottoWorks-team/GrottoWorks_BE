using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using TaskExecution.Application.Tasks;

namespace TaskExecution.Infrastructure.Identity;

public sealed class VolunteerDirectoryCacheGate(TimeProvider clock)
{
    private long pausedUntil;
    public bool IsAvailable => clock.GetUtcNow().UtcTicks >= Interlocked.Read(ref pausedUntil);
    public void Pause() => Interlocked.Exchange(ref pausedUntil, clock.GetUtcNow().AddSeconds(30).UtcTicks);
}

public sealed class CachedVolunteerDirectory(IdentityVolunteerDirectory source, IDistributedCache cache,
    VolunteerDirectoryCacheGate gate, ILogger<CachedVolunteerDirectory> logger) : IVolunteerDirectory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<VolunteerDirectoryPage> GetPageAsync(Guid parishId, Guid? communityId, int page, int size,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = $"volunteer-directory:v1:{parishId:N}:{communityId?.ToString("N") ?? "all"}:{page}:{size}";
        if (gate.IsAvailable)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(200));
                var bytes = await cache.GetAsync(key, timeout.Token);
                if (bytes is not null)
                {
                    var cached = JsonSerializer.Deserialize<VolunteerDirectoryPage>(bytes, JsonOptions);
                    if (cached is not null && IdentityVolunteerDirectory.ValidPage(cached, parishId, communityId, page, size))
                        return cached;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (IsCacheFailure(exception)) { Pause(exception); }
        }

        var result = await source.GetPageAsync(parishId, communityId, page, size, cancellationToken);
        if (gate.IsAvailable)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(200));
                await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(result, JsonOptions),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) }, timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (IsCacheFailure(exception)) { Pause(exception); }
        }
        return result;
    }

    private void Pause(Exception exception)
    {
        gate.Pause();
        logger.LogWarning("Volunteer directory cache unavailable ({ErrorType}); bypassing cache for 30 seconds.", exception.GetType().Name);
    }
    private static bool IsCacheFailure(Exception exception) => exception is StackExchange.Redis.RedisException or
        TimeoutException or OperationCanceledException or JsonException;
}
