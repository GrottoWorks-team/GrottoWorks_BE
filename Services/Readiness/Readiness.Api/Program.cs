using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Readiness.Api.Common;
using Readiness.Api.Endpoints;
using Readiness.Application;
using Readiness.Infrastructure;
using Readiness.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddValidation();
builder.Services.AddGrottoWorksSecurity(builder.Configuration);
builder.Services.AddGrottoWorksCurrentUser();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler,
    ProblemDetailsAuthorizationMiddlewareResultHandler>();

// { data, meta } envelope, 422 field validation + correlationId, exception → RFC 9457 (F-PLT-03).
builder.Services.AddGrottoWorksWeb();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails += context =>
    {
        if (context.ProblemDetails is HttpValidationProblemDetails)
        {
            context.ProblemDetails.Extensions["code"] = "VALIDATION_FAILED";
        }
    };
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IReadinessProbe, DatabaseReadinessProbe>();

// SNAKE_CASE_UPPER enum values per API Contract.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Readiness API v1");
        options.RoutePrefix = "swagger";
        options.EnablePersistAuthorization();
    });
}

app.UseGrottoWorksDefaults();
app.UseMiddleware<DomainRuleTranslationMiddleware>();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapReadinessEndpoints();
app.MapGrottoWorksHealthEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    try
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ReadinessDbContext>().Database.MigrateAsync();
    }
    catch (Exception exception)
    {
        app.Logger.LogError(
            exception,
            "Database migration failed. The service stays up; /health/ready reports Unhealthy.");
    }
}

app.Run();

/// <summary>Exposed for tests (WebApplicationFactory) and route inspection.</summary>
public partial class Program;
