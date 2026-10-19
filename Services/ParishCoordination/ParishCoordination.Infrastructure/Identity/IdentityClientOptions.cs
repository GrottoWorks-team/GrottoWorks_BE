using Microsoft.Extensions.Configuration;

namespace ParishCoordination.Infrastructure.Identity;

public sealed record IdentityClientOptions(
    Uri BaseAddress,
    string InternalApiKey)
{
    public const string BaseAddressConfigurationKey = "IdentityClient:BaseAddress";
    public const string InternalApiKeyConfigurationKey = "InternalApi:ApiKey";
    public const int MinimumApiKeyLength = 32;

    public static IdentityClientOptions FromConfiguration(IConfiguration configuration)
    {
        var configuredBaseAddress = configuration[BaseAddressConfigurationKey];
        if (!Uri.TryCreate(configuredBaseAddress, UriKind.Absolute, out var baseAddress) ||
            (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Configuration '{BaseAddressConfigurationKey}' must be an absolute HTTP or HTTPS URL.");
        }

        return new IdentityClientOptions(
            baseAddress,
            configuration[InternalApiKeyConfigurationKey] ?? string.Empty);
    }
}
