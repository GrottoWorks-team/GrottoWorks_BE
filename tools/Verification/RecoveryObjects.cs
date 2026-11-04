using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Minio;
using Minio.DataModel.Args;

internal static class RecoveryObjects
{
    public static async Task RestoreAsync(string archivePath, string bucket)
    {
        if (!bucket.StartsWith("grottoworks-restore-", StringComparison.Ordinal))
            throw new ArgumentException("Restore is restricted to a new verification bucket.");
        using var storage = new MinioClient().WithEndpoint("localhost", 9000)
            .WithCredentials(Environment.GetEnvironmentVariable("VERIFY_MINIO_USER") ?? "grottoworks",
                Environment.GetEnvironmentVariable("VERIFY_MINIO_PASSWORD")
                    ?? throw new InvalidOperationException("VERIFY_MINIO_PASSWORD is required.")).Build();
        if (await storage.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket)))
            throw new InvalidOperationException("Restore bucket already exists; no objects were overwritten.");
        await storage.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket));
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
            await using (var source = entry.Open())
                await storage.PutObjectAsync(new PutObjectArgs().WithBucket(bucket).WithObject(key)
                    .WithStreamData(source).WithObjectSize(row.GetProperty("bytes").GetInt64())
                    .WithContentType(row.GetProperty("metadata").GetProperty("ContentType").GetString()!));
            string? restoredHash = null;
            await storage.GetObjectAsync(new GetObjectArgs().WithBucket(bucket).WithObject(key)
                .WithCallbackStream(source => restoredHash = Convert.ToHexString(SHA256.HashData(source))));
            if (!expected.Equals(restoredHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Restored object checksum mismatch.");
            count++;
        }
        Console.WriteLine($"Restored and checksum-verified {count} objects in {bucket}.");
    }
}
