using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Identity.Api.Common;
using Identity.Api.Endpoints;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Infrastructure;
using Identity.Infrastructure.Authentication;
using Identity.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI + DataAnnotations validation for minimal API request DTOs (ParishCoordination pattern).
builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        // Contract 4.3: field validation failures are reported as 422, not 400.
        if (context.ProblemDetails is HttpValidationProblemDetails)
        {
            context.ProblemDetails.Status = StatusCodes.Status422UnprocessableEntity;
            context.HttpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        }

        context.ProblemDetails.Extensions["correlationId"] =
            CorrelationIdMiddlewareExtensions.GetCorrelationId(context.HttpContext);
    };
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// snake_case enum values ("ACTIVE", "INACTIVE", ...) per API Contract 4.1.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(
            JsonNamingPolicy.SnakeCaseUpper,
            allowIntegerValues: false));
});

// JWT bearer validation for the token issued by Identity.Infrastructure.TokenService.
// Claim contract: sub, role, parishId, communityId (literal names — inbound mapping disabled).
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var signingKey = jwtSection["SigningKey"]
    ?? throw new InvalidOperationException("Configuration section 'Jwt' is missing 'SigningKey'.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCorrelationId();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapSkillEndpoints();
app.MapHealthEndpoints();

// Apply migrations and seed the 5 roles + default ADMIN (F-IDN-01).
// Failure is logged, not fatal: /health/ready reports the database as unavailable.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.Database.MigrateAsync();

        await scope.ServiceProvider
            .GetRequiredService<IIdentityDataSeeder>()
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
