using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace NotificationAudit.Infrastructure.SystemJobs;

/// <summary>GWBAK001: authenticated header, independently authenticated 1 MiB chunks, authenticated EOF.
/// Header: magic(8), keyIdLength(u16 LE), UTF8 keyId, noncePrefix(8). Frame: length(u32 LE), ciphertext, tag(16).
/// Nonce = prefix + frameIndex(u32 BE). AAD = full header + frameIndex(u32 BE) + length(u32 LE).
/// The zero-length terminal frame is mandatory. Keys are externally managed, never written to artifacts.</summary>
public sealed class BackupEncryptionStream : Stream
{
    private const int ChunkBytes = 1024 * 1024;
    private readonly Stream destination;
    private readonly AesGcm cipher;
    private readonly byte[] header;
    private readonly byte[] noncePrefix;
    private readonly byte[] buffer = new byte[ChunkBytes];
    private readonly CancellationToken cancellationToken;
    private int buffered;
    private uint frameIndex;
    private bool completed;

    public BackupEncryptionStream(Stream destination, byte[] key, string keyId, CancellationToken cancellationToken = default)
    {
        if (key.Length != 32) throw new ArgumentException("A 256-bit encryption key is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(keyId) || Encoding.UTF8.GetByteCount(keyId) > 128)
            throw new ArgumentException("An external key identifier of at most 128 UTF8 bytes is required.", nameof(keyId));
        this.destination = destination;
        this.cancellationToken = cancellationToken;
        cipher = new AesGcm(key, 16);
        noncePrefix = RandomNumberGenerator.GetBytes(8);
        var identifier = Encoding.UTF8.GetBytes(keyId);
        header = new byte[8 + 2 + identifier.Length + 8];
        "GWBAK001"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), (ushort)identifier.Length);
        identifier.CopyTo(header, 10);
        noncePrefix.CopyTo(header, 10 + identifier.Length);
        destination.Write(header);
    }

    public override void Write(byte[] data, int offset, int count) => Write(data.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(completed, this);
        while (!data.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length - buffered, data.Length);
            data[..count].CopyTo(buffer.AsSpan(buffered));
            buffered += count;
            data = data[count..];
            if (buffered == buffer.Length) WriteFrame();
        }
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Write(data.Span);
        return ValueTask.CompletedTask;
    }

    private void WriteFrame()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (frameIndex == uint.MaxValue) throw new IOException("Backup encryption frame limit exceeded.");
        Span<byte> nonce = stackalloc byte[12];
        noncePrefix.CopyTo(nonce);
        BinaryPrimitives.WriteUInt32BigEndian(nonce[8..], frameIndex);
        var aad = new byte[header.Length + 8];
        header.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(header.Length), frameIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(aad.AsSpan(header.Length + 4), (uint)buffered);
        var encrypted = new byte[buffered];
        Span<byte> tag = stackalloc byte[16];
        cipher.Encrypt(nonce, buffer.AsSpan(0, buffered), encrypted, tag, aad);
        destination.Write(aad.AsSpan(header.Length + 4, 4));
        destination.Write(encrypted);
        destination.Write(tag);
        CryptographicOperations.ZeroMemory(buffer.AsSpan(0, buffered));
        buffered = 0;
        frameIndex++;
    }

    public void Complete()
    {
        if (completed) return;
        if (buffered > 0) WriteFrame();
        WriteFrame(); // Authenticated empty EOF, cannot confuse truncation with successful completion.
        destination.Flush();
        completed = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { cipher.Dispose(); CryptographicOperations.ZeroMemory(buffer); completed = true; }
        base.Dispose(disposing);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !completed;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => destination.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    // For a controlled restore process. The caller must discard all output if authentication fails.
    public static async Task<string> DecryptAsync(Stream source, Stream plaintext, byte[] key, CancellationToken ct = default)
    {
        if (key.Length != 32) throw new ArgumentException("A 256-bit key is required.", nameof(key));
        var initial = new byte[10];
        await source.ReadExactlyAsync(initial, ct);
        if (!initial.AsSpan(0, 8).SequenceEqual("GWBAK001"u8)) throw new InvalidDataException("Unknown backup format.");
        var keyLength = BinaryPrimitives.ReadUInt16LittleEndian(initial.AsSpan(8));
        if (keyLength is 0 or > 128) throw new InvalidDataException("Invalid key identifier.");
        var header = new byte[10 + keyLength + 8];
        initial.CopyTo(header, 0);
        await source.ReadExactlyAsync(header.AsMemory(10), ct);
        using var cipher = new AesGcm(key, 16);
        uint frame = 0;
        var lengthBytes = new byte[4];
        while (true)
        {
            if (frame == uint.MaxValue) throw new InvalidDataException("Frame limit exceeded.");
            await source.ReadExactlyAsync(lengthBytes, ct);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);
            if (length > ChunkBytes) throw new InvalidDataException("Invalid frame size.");
            var encrypted = new byte[(int)length];
            await source.ReadExactlyAsync(encrypted, ct);
            var tag = new byte[16];
            await source.ReadExactlyAsync(tag, ct);
            var nonce = new byte[12];
            header.AsSpan(header.Length - 8).CopyTo(nonce);
            BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), frame);
            var aad = new byte[header.Length + 8];
            header.CopyTo(aad, 0);
            BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(header.Length), frame);
            lengthBytes.CopyTo(aad, header.Length + 4);
            var decrypted = new byte[(int)length];
            cipher.Decrypt(nonce, encrypted, tag, decrypted, aad);
            if (length == 0)
            {
                if (await source.ReadAsync(new byte[1], ct) != 0) throw new InvalidDataException("Trailing backup data.");
                return Encoding.UTF8.GetString(header, 10, keyLength);
            }
            try { await plaintext.WriteAsync(decrypted, ct); }
            finally { CryptographicOperations.ZeroMemory(decrypted); }
            frame++;
        }
    }
}
