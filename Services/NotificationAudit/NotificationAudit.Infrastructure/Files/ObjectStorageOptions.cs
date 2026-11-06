using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;

namespace NotificationAudit.Infrastructure.Files;

/// <summary>Supported S3-compatible object stores. MinIO is used locally, Cloudflare R2 on staging/production.</summary>
public enum ObjectStorageProvider
{
    MinIO,
    R2
}

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    public ObjectStorageProvider Provider { get; init; } = ObjectStorageProvider.MinIO;

    /// <summary>
    /// Endpoint the services use. MinIO: <c>http://minio:9000</c>.
    /// R2: <c>https://&lt;ACCOUNT_ID&gt;.r2.cloudflarestorage.com</c> (or set <see cref="AccountId"/>).
    /// </summary>
    public string InternalEndpoint { get; init; } = string.Empty;

    /// <summary>
    /// MinIO only: endpoint embedded in presigned URLs handed to browsers (empty = <see cref="InternalEndpoint"/>).
    /// Ignored for R2, whose presigned URLs are valid only on the S3 API endpoint.
    /// </summary>
    public string PublicEndpoint { get; init; } = string.Empty;

    /// <summary>Cloudflare account ID; builds the R2 endpoint when <see cref="InternalEndpoint"/> is empty.</summary>
    public string AccountId { get; init; } = string.Empty;

    /// <summary>Signing region. Empty = <c>auto</c> for R2, <c>us-east-1</c> for MinIO.</summary>
    public string Region { get; init; } = string.Empty;

    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string Bucket { get; init; } = "grottoworks-files";

    /// <summary>
    /// Create the bucket on startup when it is missing. Null = only for MinIO. On R2 the bucket, CORS and
    /// lifecycle rules are provisioned in the Cloudflare dashboard and the API token is scoped to the bucket.
    /// </summary>
    public bool? ManageBucket { get; init; }

    public bool HasCredentials => !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(SecretKey);

    public bool ShouldManageBucket => ManageBucket ?? Provider == ObjectStorageProvider.MinIO;

    public string ResolveInternalEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(InternalEndpoint)) return InternalEndpoint.TrimEnd('/');
        return Provider switch
        {
            ObjectStorageProvider.R2 when !string.IsNullOrWhiteSpace(AccountId) =>
                $"https://{AccountId.Trim()}.r2.cloudflarestorage.com",
            ObjectStorageProvider.R2 => throw new InvalidOperationException(
                "ObjectStorage:InternalEndpoint or ObjectStorage:AccountId is required for Cloudflare R2."),
            _ => "http://minio:9000"
        };
    }

    public string ResolvePublicEndpoint() =>
        Provider == ObjectStorageProvider.R2 || string.IsNullOrWhiteSpace(PublicEndpoint)
            ? ResolveInternalEndpoint()
            : PublicEndpoint.TrimEnd('/');

    public string ResolveRegion() =>
        !string.IsNullOrWhiteSpace(Region) ? Region.Trim()
        : Provider == ObjectStorageProvider.R2 ? "auto" : "us-east-1";
}

/// <summary>Builds S3 clients that work against both MinIO and Cloudflare R2.</summary>
public static class ObjectStorageClientFactory
{
    public static IAmazonS3 Create(ObjectStorageOptions options, bool forPublicUrls = false)
    {
        var endpoint = forPublicUrls ? options.ResolvePublicEndpoint() : options.ResolveInternalEndpoint();
        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = options.ResolveRegion(),
            // R2 does not accept the SDK's default trailing CRC checksums on every request.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            Timeout = TimeSpan.FromMinutes(5),
            MaxErrorRetry = 3
        };
        return new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
    }

    public static bool IsHttps(string endpoint) =>
        new Uri(endpoint).Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

    public static Protocol ProtocolOf(string endpoint) => IsHttps(endpoint) ? Protocol.HTTPS : Protocol.HTTP;

    /// <summary>
    /// R2 rejects streaming (aws-chunked) payload signatures, so uploads over HTTPS send UNSIGNED-PAYLOAD.
    /// Over plain HTTP (local MinIO) the SDK must sign the payload.
    /// </summary>
    public static bool ShouldDisablePayloadSigning(ObjectStorageOptions options) =>
        IsHttps(options.ResolveInternalEndpoint());
}

/// <summary>
/// Process-wide S3 clients (thread-safe, reuse HTTP connections). Created lazily so a service without
/// object storage configuration still starts; the first storage call then fails as "storage unavailable".
/// </summary>
public sealed class ObjectStorageClients(IOptions<ObjectStorageOptions> configuredOptions) : IDisposable
{
    private readonly Lazy<IAmazonS3> internalClient = new(() => ObjectStorageClientFactory.Create(configuredOptions.Value));
    private readonly Lazy<IAmazonS3> publicClient = new(() => ObjectStorageClientFactory.Create(configuredOptions.Value, forPublicUrls: true));

    public ObjectStorageOptions Options => configuredOptions.Value;
    /// <summary>Client for server-side object operations.</summary>
    public IAmazonS3 Internal => internalClient.Value;
    /// <summary>Client whose endpoint is embedded in presigned URLs handed to browsers.</summary>
    public IAmazonS3 Public => publicClient.Value;

    public void Dispose()
    {
        if (internalClient.IsValueCreated) internalClient.Value.Dispose();
        if (publicClient.IsValueCreated) publicClient.Value.Dispose();
    }
}
