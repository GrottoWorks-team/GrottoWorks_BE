using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NotificationAudit.Infrastructure.SystemJobs;

public static class ArtifactStreams
{
    public static FileStream CreateTemporaryFile()
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite,
            Share = FileShare.None, Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(Path.Combine(Path.GetTempPath(), $"grottoworks-{Guid.NewGuid():N}.tmp"), options);
    }

    public static async Task<(long Bytes, string Sha256)> CopyHashedAsync(Stream source, Stream target, long limit, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (count > limit - length) throw new IOException("Source exceeded the configured size limit.");
            length += count;
            hash.AppendData(buffer.AsSpan(0, count));
            await target.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return (length, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    public static async Task<string> HashAsync(Stream stream, CancellationToken ct)
    {
        stream.Position = 0;
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        stream.Position = 0;
        return digest;
    }

    public static async Task<JsonElement> ValidateJsonLinesAsync(Stream stream, string service, CancellationToken ct)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), leaveOpen: true);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        JsonElement? footer = null;
        var began = false;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length > 4 * 1024 * 1024) throw new InvalidDataException("Source row is too large.");
            using var json = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 32 });
            var row = json.RootElement;
            var type = row.GetProperty("type").GetString();
            if (footer.HasValue) throw new InvalidDataException("Unexpected data after source footer.");
            if (type == "begin" && !began)
            {
                if (row.GetProperty("service").GetString() != service || row.GetProperty("schemaVersion").GetInt32() != 1)
                    throw new InvalidDataException("Source identity or schema does not match.");
                began = true;
            }
            else if (type == "row" && began)
            {
                var table = row.GetProperty("table").GetString()!;
                if (row.GetProperty("data").ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid source row.");
                counts[table] = counts.GetValueOrDefault(table) + 1;
            }
            else if (type == "end" && began)
            {
                if (row.GetProperty("service").GetString() != service || row.GetProperty("schemaVersion").GetInt32() != 1 ||
                    !row.GetProperty("complete").GetBoolean()) throw new InvalidDataException("Invalid source footer.");
                var declared = row.GetProperty("counts").EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetInt64());
                if (counts.Any(item => !declared.TryGetValue(item.Key, out var count) || count != item.Value) ||
                    declared.Any(item => counts.GetValueOrDefault(item.Key) != item.Value)) throw new InvalidDataException("Source row counts do not match.");
                footer = row.Clone();
            }
            else throw new InvalidDataException("Invalid source ordering.");
        }
        stream.Position = 0;
        return footer ?? throw new InvalidDataException("Source response was truncated.");
    }
}
