using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotificationAudit.Application.Files;

namespace NotificationAudit.Infrastructure.Files;

/// <summary>
/// <see cref="IFileObjectStorage"/> over the S3 API. Works with local MinIO and Cloudflare R2;
/// the provider is selected purely by <see cref="ObjectStorageOptions"/>.
/// </summary>
public sealed class S3FileObjectStorage(ObjectStorageClients clients, TimeProvider timeProvider) : IFileObjectStorage
{
    private readonly ObjectStorageOptions options = clients.Options;
    private IAmazonS3 internalClient => clients.Internal;
    private IAmazonS3 publicClient => clients.Public;

    public async Task<StoredObjectInspection> InspectAsync(string objectKey, long maximumBytes, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await internalClient.GetObjectAsync(
                new GetObjectRequest { BucketName = options.Bucket, Key = objectKey }, cancellationToken);
            if (response.ContentLength > maximumBytes) throw new UploadedObjectTooLargeException();
            return await InspectStreamAsync(response.ResponseStream, response.Headers.ContentType, maximumBytes, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or UploadedObjectTooLargeException))
        {
            throw new FileStorageUnavailableException("Could not inspect the uploaded object.", exception);
        }
    }

    public async Task UploadAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken)
    {
        try
        {
            await internalClient.PutObjectAsync(new PutObjectRequest
            {
                BucketName = options.Bucket,
                Key = objectKey,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false,
                UseChunkEncoding = false
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new FileStorageUnavailableException("Could not upload the file to object storage.", exception);
        }
    }

    public async Task CopyToPrivateAsync(string pendingObjectKey, string completedObjectKey, CancellationToken cancellationToken)
    {
        try
        {
            await internalClient.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = options.Bucket,
                SourceKey = pendingObjectKey,
                DestinationBucket = options.Bucket,
                DestinationKey = completedObjectKey
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new FileStorageUnavailableException("Could not finalize the uploaded object.", exception);
        }
    }

    public async Task<PresignedObjectRequest> CreateUploadRequestAsync(
        string objectKey,
        string contentType,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expiresAt = ExpiresAt(lifetime);
        var url = await publicClient.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = options.Bucket,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            // Signed: the browser must send exactly this Content-Type header.
            ContentType = contentType,
            Expires = expiresAt.UtcDateTime,
            Protocol = ObjectStorageClientFactory.ProtocolOf(options.ResolvePublicEndpoint())
        });
        return new PresignedObjectRequest(
            url,
            new Dictionary<string, string> { ["Content-Type"] = contentType },
            expiresAt);
    }

    public async Task<PresignedObjectRequest> CreateDownloadRequestAsync(
        string objectKey,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expiresAt = ExpiresAt(lifetime);
            var url = await publicClient.GetPreSignedURLAsync(new GetPreSignedUrlRequest
            {
                BucketName = options.Bucket,
                Key = objectKey,
                Verb = HttpVerb.GET,
                Expires = expiresAt.UtcDateTime,
                Protocol = ObjectStorageClientFactory.ProtocolOf(options.ResolvePublicEndpoint())
            });
            return new PresignedObjectRequest(url, new Dictionary<string, string>(), expiresAt);
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
            await internalClient.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = options.Bucket, Key = objectKey }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new FileStorageUnavailableException("Could not delete the object.", exception);
        }
    }

    private DateTimeOffset ExpiresAt(TimeSpan lifetime)
    {
        // Presigned URLs are valid for 1 minute .. 7 days on both MinIO and R2.
        var seconds = Math.Clamp((int)lifetime.TotalSeconds, 60, 604800);
        return timeProvider.GetUtcNow().AddSeconds(seconds);
    }

    private static string? NormalizeContentType(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? null : contentType.Split(';')[0].Trim();

    public static string DetectContentType(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "image/jpeg";
        if (content.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        if (content.StartsWith("%PDF-"u8)) return "application/pdf";
        return "application/octet-stream";
    }

    public static async Task<StoredObjectInspection> InspectStreamAsync(
        Stream stream, string? contentType, long maximumBytes, CancellationToken ct)
    {
        if (maximumBytes < 1 || maximumBytes > FileUploadLimits.DesignAssetBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[(int)Math.Min(81920, maximumBytes)];
        var header = new byte[20];
        var headerLength = 0;
        var total = 0L;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var limit = (int)Math.Min(buffer.Length, maximumBytes - total + 1);
            var count = await stream.ReadAsync(buffer.AsMemory(0, limit), ct);
            if (count == 0) break;
            total += count;
            if (total > maximumBytes) throw new UploadedObjectTooLargeException();

            hash.AppendData(buffer, 0, count);
            var prefixCount = Math.Min(count, header.Length - headerLength);
            buffer.AsSpan(0, prefixCount).CopyTo(header.AsSpan(headerLength));
            headerLength += prefixCount;
        }

        var prefix = header.AsSpan(0, headerLength);
        var detected = IsGlb(prefix, total) ? FileUploadLimits.GlbContentType : DetectContentType(prefix);
        return new StoredObjectInspection(total, NormalizeContentType(contentType),
            Convert.ToHexStringLower(hash.GetHashAndReset()), detected);
    }

    private static bool IsGlb(ReadOnlySpan<byte> header, long total)
    {
        // GLB v2 has a 12-byte file header followed by a required JSON chunk.
        if (header.Length < 20 || !header.StartsWith("glTF"u8))
            return false;

        var version = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var jsonBytes = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        var hasJsonChunk = header[16..20].SequenceEqual("JSON"u8);
        return version == 2 && declaredSize == total && hasJsonChunk &&
            jsonBytes >= 4 && jsonBytes % 4 == 0 && jsonBytes <= total - 20;
    }
}

/// <summary>
/// MinIO (local): creates the bucket if missing. R2: the bucket is provisioned in Cloudflare, so this only
/// verifies that the bucket is reachable with the configured, bucket-scoped token.
/// </summary>
internal sealed class ObjectStorageInitializer(
    ObjectStorageClients clients,
    ILogger<ObjectStorageInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = clients.Options;
        if (!options.HasCredentials)
        {
            logger.LogWarning("Object storage credentials are not configured; file upload requests will be unavailable.");
            return;
        }

        try
        {
            var client = clients.Internal;
            if (await ObjectStorageProbe.BucketExistsAsync(client, options.Bucket, cancellationToken)) return;
            if (!options.ShouldManageBucket)
            {
                logger.LogError("Object storage bucket {Bucket} on {Provider} does not exist or is not accessible.",
                    options.Bucket, options.Provider);
                return;
            }
            await client.PutBucketAsync(new PutBucketRequest { BucketName = options.Bucket }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not initialize the private object storage bucket {Bucket}.", options.Bucket);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class ObjectStorageProbe
{
    public static async Task<bool> BucketExistsAsync(IAmazonS3 client, string bucket, CancellationToken cancellationToken)
    {
        try
        {
            // ListObjectsV2 needs only object-level permissions, which is what a bucket-scoped R2 token grants.
            await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, MaxKeys = 1 }, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
