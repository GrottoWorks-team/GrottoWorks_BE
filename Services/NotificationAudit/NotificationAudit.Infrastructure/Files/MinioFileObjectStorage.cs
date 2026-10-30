using Minio;
using Minio.DataModel.Args;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotificationAudit.Application.Files;

namespace NotificationAudit.Infrastructure.Files;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";
    public string InternalEndpoint { get; init; } = "http://minio:9000";
    public string PublicEndpoint { get; init; } = "http://localhost:9000";
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string Bucket { get; init; } = "grottoworks-files";
}

internal sealed class MinioFileObjectStorage : IFileObjectStorage
{
    private readonly IMinioClient publicClient;
    private readonly ObjectStorageOptions options;
    private readonly TimeProvider timeProvider;

    public MinioFileObjectStorage(
        Microsoft.Extensions.Options.IOptions<ObjectStorageOptions> options,
        TimeProvider timeProvider)
    {
        this.options = options.Value;
        this.timeProvider = timeProvider;
        publicClient = CreateClient(this.options.PublicEndpoint, this.options.AccessKey, this.options.SecretKey);
    }

    public async Task<PresignedObjectRequest> CreateUploadRequestAsync(
        string objectKey,
        string contentType,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var seconds = Math.Clamp((int)lifetime.TotalSeconds, 60, 604800);
        var url = await publicClient.PresignedPutObjectAsync(
            new PresignedPutObjectArgs()
                .WithBucket(options.Bucket)
                .WithObject(objectKey)
                .WithExpiry(seconds));

        return new PresignedObjectRequest(
            url,
            new Dictionary<string, string> { ["Content-Type"] = contentType },
            timeProvider.GetUtcNow().AddSeconds(seconds));
    }

    internal static IMinioClient CreateClient(string endpoint, string accessKey, string secretKey)
    {
        var uri = new Uri(endpoint);
        return new MinioClient()
            .WithEndpoint(uri.Host, uri.Port)
            .WithCredentials(accessKey, secretKey)
            .WithSSL(uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            .Build();
    }
}

internal sealed class MinioBucketInitializer(
    Microsoft.Extensions.Options.IOptions<ObjectStorageOptions> configuredOptions,
    ILogger<MinioBucketInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = configuredOptions.Value;
        if (string.IsNullOrWhiteSpace(options.AccessKey) || string.IsNullOrWhiteSpace(options.SecretKey))
        {
            logger.LogWarning("Object storage credentials are not configured; file upload requests will be unavailable.");
            return;
        }

        try
        {
            var client = MinioFileObjectStorage.CreateClient(
                options.InternalEndpoint,
                options.AccessKey,
                options.SecretKey);
            var exists = await client.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(options.Bucket),
                cancellationToken);
            if (!exists)
            {
                await client.MakeBucketAsync(
                    new MakeBucketArgs().WithBucket(options.Bucket),
                    cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not initialize the private object storage bucket {Bucket}.", options.Bucket);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
