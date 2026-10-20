using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Web;

namespace TaskExecution.Api.Common;

public sealed class InternalApiKeyFilter(IConfiguration configuration, ILogger<InternalApiKeyFilter> logger)
    : IEndpointFilter
{
    public const string HeaderName = "X-Internal-Api-Key";
    public const string ConfigurationKey = "InternalApi:ApiKey";
    public const int MinimumKeyLength = 32;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = configuration[ConfigurationKey];
        if (string.IsNullOrEmpty(expected) || expected.Length < MinimumKeyLength)
        {
            logger.LogWarning("Internal API key is missing or shorter than {Length} characters.", MinimumKeyLength);
            return Reject(context.HttpContext);
        }

        var presented = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (!FixedTimeEquals(presented, expected))
        {
            return Reject(context.HttpContext);
        }

        return await next(context);
    }

    private static bool FixedTimeEquals(string presented, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    private static IResult Reject(HttpContext httpContext) =>
        Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Internal authentication required",
            detail: "A valid internal API key is required.",
            type: "https://api.grottoworks.example/problems/internal-auth-required",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "INTERNAL_AUTH_REQUIRED",
                ["correlationId"] = CorrelationIdMiddlewareExtensions.GetCorrelationId(httpContext)
            });
}
