using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web;

public sealed record IntegrationQueueState(string Queue,long Pending,long RetryAttempts);
public interface IIntegrationQueueProbe
{
    string Service {get;}
    Task<IReadOnlyList<IntegrationQueueState>> ReadAsync(CancellationToken ct);
}

/// <summary>Each API supplies only its own DbConnection and fixed, reviewed aggregate SQL.</summary>
public static class IntegrationQueueReader
{
    public static async Task<IReadOnlyList<IntegrationQueueState>> ReadAsync(DbConnection connection,
        IReadOnlyDictionary<string,string> queries,CancellationToken ct)
    {
        if(connection.State!=ConnectionState.Open)await connection.OpenAsync(ct);
        var result=new List<IntegrationQueueState>();
        foreach(var query in queries)
        {
            await using var command=connection.CreateCommand();command.CommandText=query.Value;command.CommandTimeout=5;
            await using var reader=await command.ExecuteReaderAsync(ct);
            if(await reader.ReadAsync(ct))result.Add(new(query.Key,reader.GetInt64(0),reader.GetInt64(1)));
        }
        return result;
    }
}

public sealed class IntegrationQueueMonitor(IServiceScopeFactory scopes,ILogger<IntegrationQueueMonitor> logger) : BackgroundService
{
    private static readonly ConcurrentDictionary<(string Service,string Queue),IntegrationQueueState> States=[];
    private static readonly ConcurrentDictionary<string,int> Availability=[];
    private static readonly Meter Meter=new(GrottoWorksObservability.Name);
    private static readonly ObservableGauge<long> Pending=Meter.CreateObservableGauge("grottoworks.integration.pending",()=>States.Select(row=>
        new Measurement<long>(row.Value.Pending,new KeyValuePair<string,object?>("service",row.Key.Service),new("queue",row.Key.Queue))));
    private static readonly ObservableGauge<long> Retries=Meter.CreateObservableGauge("grottoworks.integration.retry_attempts",()=>States.Select(row=>
        new Measurement<long>(row.Value.RetryAttempts,new KeyValuePair<string,object?>("service",row.Key.Service),new("queue",row.Key.Queue))));
    private static readonly ObservableGauge<int> Healthy=Meter.CreateObservableGauge("grottoworks.integration.probe_available",()=>Availability.Select(row=>
        new Measurement<int>(row.Value,new KeyValuePair<string,object?>("service",row.Key))));
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            await using var scope=scopes.CreateAsyncScope();
            foreach(var probe in scope.ServiceProvider.GetServices<IIntegrationQueueProbe>())
            {
                try
                {
                    foreach(var state in await probe.ReadAsync(stoppingToken))States[(probe.Service,state.Queue)]=state;
                    Availability[probe.Service]=1;
                }
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){return;}
                catch(Exception exception)
                {
                    Availability[probe.Service]=0;
                    logger.LogWarning("Integration queue probe for {Service} is unavailable ({ErrorType}).",probe.Service,exception.GetType().Name);
                }
            }
        }while(await timer.WaitForNextTickAsync(stoppingToken));
    }
}
