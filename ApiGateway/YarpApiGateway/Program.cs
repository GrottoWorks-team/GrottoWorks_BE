using System.Threading.RateLimiting;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// JWT validation (F-PLT-06) — same claim contract as Identity issues: sub/role/parishId/communityId.
builder.Services.AddGrottoWorksSecurity(builder.Configuration);

// Route-level authorization in appsettings.json references this policy.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());
});

// Fixed-window rate limiting (F-PLT-06) for auth-heavy routes: 5 requests / 10 s per client IP.
// Partitioned by remote IP so one client cannot exhaust the quota for everyone.
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    rateLimiterOptions.AddPolicy("fixed", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromSeconds(10),
                PermitLimit = 5,
                QueueLimit = 0
            }));
});

// CORS (F-PLT-06): browsers (FE dev server, Swagger UI of each service) call the gateway cross-origin.
// Allowed origins come from config "Cors:AllowedOrigins" (env: Cors__AllowedOrigins__0, __1, ...).
const string CorsPolicyName = "grottoworks-frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Let the browser read the correlation id returned by every service (README §3).
        .WithExposedHeaders("X-Correlation-Id"));
});

// Routes/clusters are defined under "ReverseProxy" in appsettings.json.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Generate/propagate X-Correlation-Id before proxying so every service sees the same id (README §3).
app.UseCorrelationId();
// Before authentication so CORS preflight (OPTIONS, no token) is answered instead of returning 401.
app.UseCors(CorsPolicyName);
// Explicit so the bearer token is parsed before YARP evaluates per-route AuthorizationPolicy.
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapReverseProxy();

app.Run();
