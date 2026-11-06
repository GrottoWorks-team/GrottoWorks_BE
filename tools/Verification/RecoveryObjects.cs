using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon.S3.Model;
using NotificationAudit.Infrastructure.Files;

internal static class RecoveryObjects
{
    public static async Task RestoreAsync(string archivePath, string bucket)
    {
        if (!bucket.StartsWith("grottoworks-restore-", StringComparison.Ordinal))
            throw new ArgumentException("Restore is restricted to a new verification bucket.");
        var options = new ObjectStorageOptions
        {
            Provider = Enum.Parse<ObjectStorageProvider>(Environment.GetEnvironmentVariable("VERIFY_STORAGE_PROVIDER") ?? "MinIO", true),
            InternalEndpoint = Environment.GetEnvironmentVariable("VERIFY_STORAGE_ENDPOINT") ?? "http://localhost:9000",
            AccessKey = Environment.GetEnvironmentVariable("VERIFY_STORAGE_ACCESS_KEY")
                ?? Environment.GetEnvironmentVariable("VERIFY_MINIO_USER") ?? "grottoworks",
            SecretKey = Environment.GetEnvironmentVariable("VERIFY_STORAGE_SECRET_KEY")
                ?? Environment.GetEnvironmentVariable("VERIFY_MINIO_PASSWORD")
                ?? throw new InvalidOperationException("VERIFY_STORAGE_SECRET_KEY (or VERIFY_MINIO_PASSWORD) is required."),
            Bucket = bucket
        };
        using var storage = ObjectStorageClientFactory.Create(options);
        if (options.Provider == ObjectStorageProvider.MinIO)
        {
            if (await ObjectStorageProbe.BucketExistsAsync(storage, bucket, CancellationToken.None))
                throw new InvalidOperationException("Restore bucket already exists; no objects were overwritten.");
            await storage.PutBucketAsync(new PutBucketRequest { BucketName = bucket });
        }
        else
        {
            // R2 tokens are bucket-scoped: the restore bucket is created in the Cloudflare dashboard and must be empty.
            var existing = await storage.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, MaxKeys = 1 });
            if (existing.KeyCount > 0)
                throw new InvalidOperationException("Restore bucket is not empty; no objects were overwritten.");
        }
        using var archive = ZipFile.OpenRead(archivePath);
        using var manifest = JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open());
        var count = 0;
        foreach (var row in manifest.RootElement.GetProperty("sources").EnumerateArray())
        {
            if (!row.TryGetProperty("kind", out var kind) || kind.GetString() != "FILE_OBJECT") continue;
            if (row.GetProperty("status").GetString() != "COMPLETED")
                throw new InvalidDataException("Backup contains an incomplete object.");
            var key = row.GetProperty("objectKey").GetString()!;
            var expected = row.GetProperty("sha256").GetString()!;
            var entry = archive.GetEntry(row.GetProperty("entry").GetString()!)!;
            // Zip entry streams are not seekable; buffer so the SDK can sign a known content length.
            using var content = new MemoryStream();
            await using (var source = entry.Open()) await source.CopyToAsync(content);
            content.Position = 0;
            await storage.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = content,
                AutoCloseStream = false,
                ContentType = row.GetProperty("metadata").GetProperty("ContentType").GetString()!,
                DisablePayloadSigning = ObjectStorageClientFactory.ShouldDisablePayloadSigning(options)
            });
            string restoredHash;
            using (var restored = await storage.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = key }))
                restoredHash = Convert.ToHexString(await SHA256.HashDataAsync(restored.ResponseStream));
            if (!expected.Equals(restoredHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Restored object checksum mismatch.");
            count++;
        }
        Console.WriteLine($"Restored and checksum-verified {count} objects in {bucket}.");
    }
}
