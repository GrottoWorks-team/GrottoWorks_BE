using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskExecution.Application.Attendance;

namespace TaskExecution.Infrastructure.Attendance;

public sealed class AttendanceAlertWorker(IServiceScopeFactory scopes,IConfiguration config,ILogger<AttendanceAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollSeconds=config.GetValue("Attendance:AlertPollSeconds",60);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AttendanceAlertProcessor>().ScanAsync(DateTimeOffset.UtcNow,stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception,"Could not record long-open attendance alerts; will retry."); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1,pollSeconds)),stoppingToken);
        }
    }
}
