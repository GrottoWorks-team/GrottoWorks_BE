using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Web;

/// <summary>Low-cardinality diagnostics shared by HTTP APIs and durable background operations.</summary>
public static class GrottoWorksObservability
{
    public const string Name = "GrottoWorks.Integration";
    public static readonly ActivitySource Activities = new(Name);
    public static readonly Meter Meter = new(Name);
    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>("grottoworks.integration.operations", "{operation}");
    private static readonly Histogram<double> Durations = Meter.CreateHistogram<double>("grottoworks.integration.duration", "s");

    public static IServiceCollection AddGrottoWorksObservability(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        if (!configuration.GetValue("Observability:Enabled", false)) return services;
        if (string.IsNullOrWhiteSpace(serviceName)) throw new ArgumentException("A service name is required.", nameof(serviceName));
        services.AddHostedService<IntegrationQueueMonitor>();
        services.AddOpenTelemetry()
            .ConfigureResource(resource=>resource.AddService(serviceName))
            .WithTracing(tracing=>tracing
                .AddSource(Name,"MassTransit")
                .AddAspNetCoreInstrumentation(options=>options.Filter=context=>!context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics=>metrics
                .AddMeter(Name,"MassTransit","Microsoft.AspNetCore.Hosting","System.Net.Http")
                .AddOtlpExporter());
        services.AddLogging(logging=>logging.AddOpenTelemetry(options=>
        {
            options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName));
            options.IncludeScopes=true;
            options.IncludeFormattedMessage=false;
            options.AddOtlpExporter();
        }));
        return services;
    }

    public static Measurement Track(string operation, string transport)
        => new Measurement(operation, transport);

    public sealed class Measurement : IDisposable
    {
        private readonly long started = Stopwatch.GetTimestamp();
        private readonly string operation;
        private readonly string transport;
        private readonly Activity? activity;
        private bool failed;
        private bool disposed;
        internal Measurement(string operation, string transport)
        {
            this.operation=operation; this.transport=transport;
            activity=Activities.StartActivity(operation,ActivityKind.Internal);
            activity?.SetTag("messaging.operation.type",operation);
            activity?.SetTag("messaging.system",transport);
        }
        public void Fail()
        {
            failed=true; activity?.SetStatus(ActivityStatusCode.Error);
        }
        public void Dispose()
        {
            if(disposed) return;
            disposed=true;
            var tags=new TagList { {"operation",operation},{"transport",transport},{"outcome",failed ? "failed" : "succeeded"} };
            Outcomes.Add(1,tags); Durations.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,tags);
            activity?.Dispose();
        }
    }
}
