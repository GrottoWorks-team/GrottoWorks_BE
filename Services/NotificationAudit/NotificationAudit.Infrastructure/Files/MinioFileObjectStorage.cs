using Minio;
using Minio.DataModel.Args;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
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

public sealed class MinioFileObjectStorage : IFileObjectStorage
{
    private readonly IMinioClient publicClient;
    private readonly IMinioClient internalClient;
    private readonly ObjectStorageOptions options;
    private readonly TimeProvider timeProvider;

    public MinioFileObjectStorage(
        Microsoft.Extensions.Options.IOptions<ObjectStorageOptions> options,
        TimeProvider timeProvider)
    {
        this.options = options.Value;
        this.timeProvider = timeProvider;
        publicClient = CreateClient(this.options.PublicEndpoint, this.options.AccessKey, this.options.SecretKey);
        internalClient = CreateClient(this.options.InternalEndpoint, this.options.AccessKey, this.options.SecretKey);
    }

    public async Task<StoredObjectInspection> InspectAsync(
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var stat = await internalClient.StatObjectAsync(
                new StatObjectArgs().WithBucket(options.Bucket).WithObject(objectKey),
                cancellationToken);

            using var content = new MemoryStream(capacity: checked((int)Math.Min(stat.Size, 20L * 1024 * 1024)));
            await internalClient.GetObjectAsync(
                new GetObjectArgs()
                    .WithBucket(options.Bucket)
                    .WithObject(objectKey)
                    .WithCallbackStream(stream => stream.CopyTo(content)),
                cancellationToken);

            var bytes = content.ToArray();
            var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new StoredObjectInspection(
                stat.Size,
                stat.ContentType,
                checksum,
                DetectContentType(bytes));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new FileStorageUnavailableException("Could not inspect the uploaded object.", exception);
        }
    }

    public async Task CommitUploadAsync(
        string pendingObjectKey,
        string completedObjectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var source = new CopySourceObjectArgs()
                .WithBucket(options.Bucket)
                .WithObject(pendingObjectKey);
            await internalClient.CopyObjectAsync(
                new CopyObjectArgs()
                    .WithBucket(options.Bucket)
                    .WithObject(completedObjectKey)
                    .WithCopyObjectSource(source),
                cancellationToken);
            await internalClient.RemoveObjectAsync(
                new RemoveObjectArgs().WithBucket(options.Bucket).WithObject(pendingObjectKey),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new FileStorageUnavailableException("Could not finalize the uploaded object.", exception);
        }
    }

    public async Task<PresignedObjectRequest> CreateDownloadRequestAsync(
        string objectKey,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seconds = Math.Clamp((int)lifetime.TotalSeconds, 60, 604800);
            var url = await publicClient.PresignedGetObjectAsync(
                new PresignedGetObjectArgs()
                    .WithBucket(options.Bucket)
                    .WithObject(objectKey)
                    .WithExpiry(seconds));
            return new PresignedObjectRequest(
                url,
                new Dictionary<string, string>(),
                timeProvider.GetUtcNow().AddSeconds(seconds));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new FileStorageUnavailableException("Could not create a download URL.", exception);
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            await internalClient.RemoveObjectAsync(
                new RemoveObjectArgs().WithBucket(options.Bucket).WithObject(objectKey),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new FileStorageUnavailableException("Could not delete the object.", exception);
        }
    }

    public static string DetectContentType(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "image/jpeg";
        if (content.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        if (content.StartsWith("%PDF-"u8)) return "application/pdf";
        return "application/octet-stream";
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
