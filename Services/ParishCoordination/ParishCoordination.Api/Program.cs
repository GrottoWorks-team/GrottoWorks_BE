using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ParishCoordination.Api.Common;
using ParishCoordination.Api.Endpoints;
using ParishCoordination.Application;
using ParishCoordination.Infrastructure;
using ParishCoordination.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI + DataAnnotations validation for minimal API request DTOs (ParishCoordination pattern).
builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddValidation();
builder.Services.AddGrottoWorksSecurity(builder.Configuration);
builder.Services.AddGrottoWorksCurrentUser();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler,
    ProblemDetailsAuthorizationMiddlewareResultHandler>();

// Cross-cutting defaults (F-PLT-03): { data, meta } envelope plumbing, ProblemDetails with 422
// field-validation status + correlationId, exception -> RFC 9457 with a stable code.
builder.Services.AddGrottoWorksWeb();

// Stable code for field-validation problems (API Contract 4.3). The += keeps the BuildingBlocks
// customization (422 status + correlationId) registered above in the chain.
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

// /health/ready probe (F-PLT-03).
builder.Services.AddScoped<IReadinessProbe, DatabaseReadinessProbe>();

// snake_case enum values ("ACTIVE", "INACTIVE", ...) per API Contract 4.1.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(
            JsonNamingPolicy.SnakeCaseUpper,
            allowIntegerValues: false));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Interactive API docs (Swagger UI, F-PLT-08) at /swagger — Development only.
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "ParishCoordination API v1");
        options.RoutePrefix = "swagger";
    });
}

// Correlation id first, exception handler second so every error carries the id (F-PLT-03).
app.UseGrottoWorksDefaults();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapParishEndpoints();
app.MapCommunityEndpoints();
app.MapSeasonEndpoints();
app.MapGrottoWorksHealthEndpoints();

// Apply pending migrations at startup so `docker compose up` yields a ready service.
// Failure is logged, not fatal: /health/ready reports the database as unavailable.
// Production must use a controlled migration process instead of this flag (F-PLT-02 note).
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ParishCoordinationDbContext>();
        await dbContext.Database.MigrateAsync();
        await scope.ServiceProvider
            .GetRequiredService<IParishCoordinationDataSeeder>()
            .SeedAsync();
    }
    catch (Exception exception)
    {
        app.Logger.LogError(
            exception,
            "Database migration/seed failed. The service stays up; /health/ready reports Unhealthy.");
    }
}

app.Run();
