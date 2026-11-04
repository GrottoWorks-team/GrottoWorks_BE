using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NotificationAudit.Infrastructure.SystemJobs;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class SystemArtifactTests
{
    [Fact]
    public async Task Backup_encryption_roundtrips_multiple_chunks_and_hides_plaintext()
    {
        var plaintext = new byte[2 * 1024 * 1024 + 31];
        RandomNumberGenerator.Fill(plaintext);
        Encoding.UTF8.GetBytes("sensitive-backup-marker").CopyTo(plaintext, 0);
        var key = RandomNumberGenerator.GetBytes(32);
        using var output = new MemoryStream();
        using (var encrypt = new BackupEncryptionStream(output, key, "secret-store/key-version-3"))
        {
            await encrypt.WriteAsync(plaintext);
            encrypt.Complete();
        }
        Encoding.UTF8.GetString(output.ToArray()).Should().NotContain("sensitive-backup-marker");
        output.Position = 0;
        using var restored = new MemoryStream();
        (await BackupEncryptionStream.DecryptAsync(output, restored, key)).Should().Be("secret-store/key-version-3");
        restored.ToArray().Should().Equal(plaintext);
    }

    [Theory]
    [InlineData("header")]
    [InlineData("ciphertext")]
    [InlineData("truncate")]
    [InlineData("append")]
    [InlineData("wrong-key")]
    public async Task Modified_or_truncated_backup_never_authenticates(string change)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        using var output = new MemoryStream();
        using (var encrypt = new BackupEncryptionStream(output, key, "key-3"))
        { encrypt.Write(Encoding.UTF8.GetBytes("recovery-data")); encrypt.Complete(); }
        var bytes = output.ToArray();
        if (change == "header") bytes[10] ^= 1;
        if (change == "ciphertext") bytes[30] ^= 1;
        if (change == "truncate") bytes = bytes[..^20]; // strip authenticated EOF
        if (change == "append") bytes = [.. bytes, 42];
        if (change == "wrong-key") key = RandomNumberGenerator.GetBytes(32);
        using var source = new MemoryStream(bytes);
        using var restored = new MemoryStream();
        Func<Task> restore = () => BackupEncryptionStream.DecryptAsync(source, restored, key);
        await restore.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Jsonl_requires_verified_footer_and_matching_record_counts()
    {
        const string begin = "{\"type\":\"begin\",\"service\":\"Identity\",\"schemaVersion\":1}\n";
        const string row = "{\"type\":\"row\",\"table\":\"app_user\",\"data\":{\"user_id\":\"id\"}}\n";
        const string end = "{\"type\":\"end\",\"service\":\"Identity\",\"schemaVersion\":1,\"complete\":true,\"counts\":{\"app_user\":1}}\n";
        using var good = new MemoryStream(Encoding.UTF8.GetBytes(begin + row + end));
        (await ArtifactStreams.ValidateJsonLinesAsync(good, "Identity", default)).GetProperty("complete").GetBoolean().Should().BeTrue();
        foreach (var invalid in new[] { begin + row, begin + row + end.Replace(":1}", ":2}"), begin + row + end + row, begin + end, row + end })
        {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(invalid));
            Func<Task> validate = () => ArtifactStreams.ValidateJsonLinesAsync(source, "Identity", default);
            await validate.Should().ThrowAsync<Exception>();
        }
    }

    [Fact]
    public async Task Byte_limit_is_enforced_without_trusting_source_length()
    {
        using var source = new MemoryStream(new byte[101]);
        using var output = new MemoryStream();
        Func<Task> copy = () => ArtifactStreams.CopyHashedAsync(source, output, 100, default);
        await copy.Should().ThrowAsync<IOException>();
        output.Length.Should().Be(0);
    }
}
