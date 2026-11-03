using System.Diagnostics.Metrics;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Platform.UnitTests;

public sealed class IntegrationDiagnosticsTests
{
    [Theory]
    [InlineData("",false)]
    [InlineData("end-to-end-correlation",true)]
    [InlineData("untrusted\r\nheader",false)]
    public async Task Correlation_is_preserved_or_replaced_before_downstream_execution(string value,bool preserve)
    {
        var http=new DefaultHttpContext();http.Request.Headers[CorrelationIdMiddleware.HeaderName]=value;
        var seen=false;
        await new CorrelationIdMiddleware(context=>
        {
            var correlation=CorrelationIdMiddlewareExtensions.GetCorrelationId(context);
            if(preserve) Assert.Equal(value,correlation);else Assert.True(Guid.TryParse(correlation,out _));
            Assert.Equal(correlation,context.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString());seen=true;
            return Task.CompletedTask;
        }).InvokeAsync(http);
        Assert.True(seen);
    }
    [Fact]
    public void Failed_operation_records_one_outcome_with_bounded_tags_even_if_disposed_twice()
    {
        var outcomes=new List<long>();var labels=new List<string>();
        using var listener=new MeterListener();
        listener.InstrumentPublished=(instrument,current)=>
        {if(instrument.Meter.Name==GrottoWorksObservability.Name && instrument.Name=="grottoworks.integration.operations")current.EnableMeasurementEvents(instrument);};
        listener.SetMeasurementEventCallback<long>((instrument,value,tags,state)=>
        {
            if(!tags.ToArray().Any(tag=>tag.Key=="operation" && Equals(tag.Value,"durable-test")))return;
            outcomes.Add(value);labels.AddRange(tags.ToArray().Select(tag=>$"{tag.Key}={tag.Value}"));
        });
        listener.Start();
        var operation=GrottoWorksObservability.Track("durable-test","outbox");operation.Fail();operation.Dispose();operation.Dispose();
        Assert.Equal(new long[] {1},outcomes);Assert.Contains("outcome=failed",labels);
        Assert.DoesNotContain(labels,value=>value.Contains("eventId",StringComparison.OrdinalIgnoreCase));
    }
}
