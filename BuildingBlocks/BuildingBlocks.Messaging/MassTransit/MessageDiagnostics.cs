using System.Diagnostics;
using System.Diagnostics.Metrics;
using MassTransit;

namespace BuildingBlocks.Messaging.MassTransit;

public static class MessageDiagnostics
{
    private static readonly Meter Meter = new("GrottoWorks.Integration");
    private static readonly Counter<long> Deliveries = Meter.CreateCounter<long>("grottoworks.messaging.deliveries","{message}");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("grottoworks.messaging.duration","s");

    internal static void Record(string messageType,string operation,long started,bool failed)
    {
        var tags=new TagList {{"message.type",messageType},{"operation",operation},{"outcome",failed ? "failed" : "succeeded"}};
        Deliveries.Add(1,tags); Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,tags);
    }

    // Published contracts use an opaque correlation string; do not require every event assembly to reference the broker runtime.
    internal static string? Correlation<T>(T message) where T:class
    {
        var value=typeof(T).GetProperty("CorrelationId")?.GetValue(message) as string;
        return !string.IsNullOrWhiteSpace(value) && value.Length<=200 && !value.Any(char.IsControl) ? value : null;
    }
}

public sealed class MessagePublishDiagnostics<T> : IFilter<PublishContext<T>> where T:class
{
    public void Probe(ProbeContext context)=>context.CreateFilterScope("grottoworks-publish-diagnostics");
    public async Task Send(PublishContext<T> context,IPipe<PublishContext<T>> next)
    {
        var correlation=MessageDiagnostics.Correlation(context.Message);
        if(correlation is not null)
        {
            context.Headers.Set("X-Correlation-Id",correlation);
            if(Guid.TryParse(correlation,out var id)) context.CorrelationId=id;
        }
        var started=Stopwatch.GetTimestamp();var failed=false;
        try {await next.Send(context);}
        catch {failed=true;throw;}
        finally {MessageDiagnostics.Record(typeof(T).Name,"publish",started,failed);}
    }
}

public sealed class MessageConsumeDiagnostics<T> : IFilter<ConsumeContext<T>> where T:class
{
    public void Probe(ProbeContext context)=>context.CreateFilterScope("grottoworks-consume-diagnostics");
    public async Task Send(ConsumeContext<T> context,IPipe<ConsumeContext<T>> next)
    {
        var correlation=context.Headers.Get<string>("X-Correlation-Id") ?? MessageDiagnostics.Correlation(context.Message);
        if(correlation is not null && correlation.Length<=200 && !correlation.Any(char.IsControl))
            Activity.Current?.SetTag("grottoworks.correlation_id",correlation);
        var started=Stopwatch.GetTimestamp();var failed=false;
        try {await next.Send(context);}
        catch {failed=true;throw;}
        finally {MessageDiagnostics.Record(typeof(T).Name,"consume",started,failed);}
    }
}
