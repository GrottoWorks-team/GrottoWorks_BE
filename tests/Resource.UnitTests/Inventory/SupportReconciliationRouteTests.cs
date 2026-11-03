using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Resource.Api.Endpoints;
using Resource.Application.Inventory;

namespace Resource.UnitTests.Inventory;

public sealed class SupportReconciliationRouteTests
{
    [Fact]
    public async Task Lookup_routes_are_internal_hidden_and_expose_read_only_contracts()
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions {EnvironmentName="Testing"});
        builder.Services.AddScoped<SupportReconciliationQueries>();
        await using var app=builder.Build();app.MapInternalSupportReconciliationEndpoints();
        var routes=((IEndpointRouteBuilder)app).DataSources.SelectMany(source=>source.Endpoints).OfType<RouteEndpoint>().ToList();
        routes.Should().HaveCount(2);
        foreach(var route in routes)
        {
            route.RoutePattern.RawText.Should().StartWith("/internal/");
            route.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()!.ExcludeFromDescription.Should().BeTrue();
            route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Should().Equal("GET");
            route.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(meta=>meta.StatusCode).Should().Contain(401);
        }
    }
}
