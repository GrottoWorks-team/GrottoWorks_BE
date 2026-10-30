using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TaskExecution.Api.Endpoints;
using TaskExecution.Application.Attendance;

namespace TaskExecution.UnitTests.Api;

public sealed class AttendanceRouteTests
{
    [Fact]
    public async Task Check_in_routes_require_volunteer_and_declare_errors()
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions {EnvironmentName="Testing"});
        builder.Services.AddScoped<AttendanceService>();
        await using var app=builder.Build();
        app.MapAttendanceEndpoints();
        var endpoints=((IEndpointRouteBuilder)app).DataSources.SelectMany(s=>s.Endpoints).OfType<RouteEndpoint>().ToList();
        foreach(var name in new[] {"requestCheckIn","cancelCheckInRequest"})
        {
            var route=endpoints.Single(e=>e.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName==name);
            route.Metadata.GetOrderedMetadata<AuthorizationPolicy>().SelectMany(p=>p.Requirements)
                .OfType<RolesAuthorizationRequirement>().Should().Contain(r=>r.AllowedRoles.Contains("VOLUNTEER"));
            route.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(p=>p.StatusCode)
                .Should().Contain(new[] {400,401,403,404,409,412,422,503});
        }
    }
}
