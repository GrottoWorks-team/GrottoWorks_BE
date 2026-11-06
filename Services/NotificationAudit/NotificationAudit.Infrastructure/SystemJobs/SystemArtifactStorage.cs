using System.Security.Cryptography;
using Amazon.S3.Model;
using NotificationAudit.Application.SystemJobs;
using NotificationAudit.Infrastructure.Files;

namespace NotificationAudit.Infrastructure.SystemJobs;

public sealed class SystemArtifactStorage(ObjectStorageClients clients) : ISystemArtifactStorage
{
    public async Task UploadAsync(string key, GeneratedSystemArtifact artifact, CancellationToken ct)
    {
        var options = clients.Options;
        var client = clients.Internal;
        artifact.Content.Position = 0;
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = options.Bucket,
            Key = key,
            InputStream = artifact.Content,
            AutoCloseStream = false,
            ContentType = artifact.ContentType,
            DisablePayloadSigning = ObjectStorageClientFactory.ShouldDisablePayloadSigning(options)
        }, ct);
        var metadata = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = options.Bucket, Key = key }, ct);
        if (metadata.ContentLength != artifact.Length) throw new IOException("Artifact storage verification failed.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var response = await client.GetObjectAsync(new GetObjectRequest { BucketName = options.Bucket, Key = key }, ct))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await response.ResponseStream.ReadAsync(buffer, ct)) > 0) hash.AppendData(buffer.AsSpan(0, read));
        }
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Artifact storage verification failed.");
    }

    public async Task DeleteAsync(string key, CancellationToken ct) =>
        await clients.Internal.DeleteObjectAsync(new DeleteObjectRequest { BucketName = clients.Options.Bucket, Key = key }, ct);
}
