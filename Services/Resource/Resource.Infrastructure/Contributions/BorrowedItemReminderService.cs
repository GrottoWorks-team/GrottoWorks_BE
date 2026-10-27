using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Resource.Application.Contributions;

namespace Resource.Infrastructure.Contributions;

/// <summary>
/// Runs <see cref="BorrowedItemReminderJob"/> every <c>BorrowedItems:ReminderIntervalMinutes</c> (default 60). The job
/// itself keeps one reminder per item per day, so several instances or restarts do not duplicate reminders.
/// Disabled with <c>BorrowedItems:RemindersEnabled=false</c> (tests, migrations).
/// </summary>
public sealed class BorrowedItemReminderService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BorrowedItemReminderService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("BorrowedItems:RemindersEnabled", true))
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("BorrowedItems:ReminderIntervalMinutes", 60)));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var reminded = await scope.ServiceProvider.GetRequiredService<BorrowedItemReminderJob>().RunAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (reminded > 0)
                {
                    logger.LogInformation("Borrowed-item reminders sent for {Count} item(s).", reminded);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Borrowed-item reminder run failed; retrying at the next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
