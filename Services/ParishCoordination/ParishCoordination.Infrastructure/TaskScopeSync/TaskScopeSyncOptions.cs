using Microsoft.Extensions.Configuration;

namespace ParishCoordination.Infrastructure.TaskScopeSync;

public sealed record TaskScopeSyncOptions(bool Enabled, Uri BaseAddress, string ApiKey,
    TimeSpan PollInterval, TimeSpan RequestTimeout)
{
    public static TaskScopeSyncOptions FromConfiguration(IConfiguration configuration)
    {
        var enabled = configuration.GetValue("TaskScopeSync:Enabled", false);
        var address = configuration["TaskScopeSync:BaseAddress"] ?? "http://localhost:6104/";
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            throw new InvalidOperationException("TaskScopeSync:BaseAddress must be an absolute HTTP(S) URL.");
        }

        var key = configuration["InternalApi:ApiKey"] ?? string.Empty;
        if (enabled && key.Length < 32)
        {
            throw new InvalidOperationException("TaskScopeSync requires InternalApi:ApiKey with at least 32 characters.");
        }

        var poll = configuration.GetValue("TaskScopeSync:PollIntervalSeconds", 2);
        var timeout = configuration.GetValue("TaskScopeSync:RequestTimeoutSeconds", 10);
        if (poll < 1 || timeout < 1)
        {
            throw new InvalidOperationException("TaskScopeSync intervals must be positive.");
        }

        return new(enabled, uri, key, TimeSpan.FromSeconds(poll), TimeSpan.FromSeconds(timeout));
    }

    public static TimeSpan RetryDelay(int attempts) => TimeSpan.FromSeconds(attempts switch
    {
        <= 1 => 2,
        2 => 5,
        3 => 15,
        4 => 60,
        _ => 300
    });
}
