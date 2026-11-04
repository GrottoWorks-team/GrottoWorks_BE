using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using NotificationAudit.Application.SystemJobs;
using NotificationAudit.Infrastructure.Files;

namespace NotificationAudit.Infrastructure.SystemJobs;

public sealed class SystemArtifactStorage(IOptions<ObjectStorageOptions> configuredOptions) : ISystemArtifactStorage
{
    private readonly ObjectStorageOptions options = configuredOptions.Value;

    public async Task UploadAsync(string key, GeneratedSystemArtifact artifact, CancellationToken ct)
    {
        using var client = MinioFileObjectStorage.CreateClient(options.InternalEndpoint, options.AccessKey, options.SecretKey);
        artifact.Content.Position = 0;
        await client.PutObjectAsync(new PutObjectArgs().WithBucket(options.Bucket).WithObject(key)
            .WithStreamData(artifact.Content).WithObjectSize(artifact.Length).WithContentType(artifact.ContentType), ct);
        var stat = await client.StatObjectAsync(new StatObjectArgs().WithBucket(options.Bucket).WithObject(key), ct);
        if (stat.Size != artifact.Length) throw new IOException("Artifact storage verification failed.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await client.GetObjectAsync(new GetObjectArgs().WithBucket(options.Bucket).WithObject(key).WithCallbackStream(stream =>
        {
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer.AsSpan(0, read)); }
        }), ct);
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Artifact storage verification failed.");
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        using var client = MinioFileObjectStorage.CreateClient(options.InternalEndpoint, options.AccessKey, options.SecretKey);
        await client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(options.Bucket).WithObject(key), ct);
    }
}
