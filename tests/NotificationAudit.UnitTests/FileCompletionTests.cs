using System.Buffers.Binary;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using NotificationAudit.Application.Files;
using NotificationAudit.Domain.Files;
using NotificationAudit.Infrastructure.Files;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class FileCompletionTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0x00 }, "image/jpeg")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, "application/pdf")]
    public void Content_type_is_detected_from_file_signature(byte[] content, string expected) =>
        S3FileObjectStorage.DetectContentType(content).Should().Be(expected);

    [Fact]
    public void Arbitrary_content_is_not_treated_as_a_supported_file() =>
        S3FileObjectStorage.DetectContentType("not-a-file"u8).Should().Be("application/octet-stream");

    [Theory]
    [InlineData("model/gltf-binary", 21 * 1024 * 1024, false)]
    [InlineData("application/octet-stream", FileUploadLimits.DesignAssetBytes, true)]
    public async Task Large_glb_completes_via_signed_upload_or_proxy(string contentType, int size, bool viaProxy)
    {
        var fixture = new FileFinalizationTests.Fixture(contentType, size, FilePurpose.DesignAsset);
        await using var file = CreateGlb(size);
        fixture.Storage.OnInspect = async limit =>
        {
            limit.Should().Be(size);
            file.Position = 0;
            return await S3FileObjectStorage.InspectStreamAsync(file, contentType, limit, default);
        };
        fixture.Storage.OnUpload = async stream =>
        {
            stream.Should().BeOfType<FileStream>();
            var body = await S3FileObjectStorage.InspectStreamAsync(stream, contentType, size, default);
            body.DetectedContentType.Should().Be(FileUploadLimits.GlbContentType);
        };

        var result = viaProxy
            ? await new ProxyUploadPendingHandler(fixture.User, fixture.Repository, fixture.Storage, fixture.Complete)
                .HandleAsync(fixture.File.Id, file, contentType, default)
            : await fixture.Complete.HandleAsync(fixture.File.Id, new(null), default);
        result.SizeBytes.Should().Be(size);
        fixture.File.Status.Should().Be(FileObjectStatus.Uploaded);
        fixture.Storage.Uploads.Should().Be(viaProxy ? 1 : 0);
    }

    [Fact]
    public async Task Invalid_binary_design_and_wrong_checksum_are_rejected()
    {
        const string type = FileUploadLimits.BinaryContentType;
        var fixture = new FileFinalizationTests.Fixture(type, 24, FilePurpose.DesignAsset);
        fixture.Storage.Inspection = new(24, type, new string('a', 64), type);
        async Task CompleteAsync() => await fixture.Complete.HandleAsync(fixture.File.Id, new(new string('b', 64)), default);
        (await Assert.ThrowsAsync<DomainException>(CompleteAsync)).Code.Should().Be("FILE_CONTENT_TYPE_MISMATCH");
        fixture.Storage.Inspection = fixture.Storage.Inspection with { DetectedContentType = FileUploadLimits.GlbContentType };
        (await Assert.ThrowsAsync<DomainException>(CompleteAsync)).Code.Should().Be("FILE_CHECKSUM_MISMATCH");
    }

    [Fact]
    public async Task Proxy_rejects_a_short_body_before_writing_to_storage()
    {
        var fixture = new FileFinalizationTests.Fixture();
        using var body = new MemoryStream([1, 2, 3]);
        var proxy = new ProxyUploadPendingHandler(fixture.User, fixture.Repository, fixture.Storage, fixture.Complete);
        var error = await Assert.ThrowsAsync<DomainException>(() =>
            proxy.HandleAsync(fixture.File.Id, body, "image/jpeg", default));
        error.Code.Should().Be("FILE_SIZE_MISMATCH");
        fixture.Storage.Uploads.Should().Be(0);
    }

    private static FileStream CreateGlb(int size)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test-glb-{Guid.NewGuid():N}.tmp");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            81920, FileOptions.DeleteOnClose);
        Span<byte> header = stackalloc byte[20];
        "glTF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 2);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], size);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], 4);
        "JSON"u8.CopyTo(header[16..]);
        stream.Write(header);
        stream.Write("{}  "u8);
        if (size > 24)
        {
            Span<byte> bin = stackalloc byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(bin, size - 32);
            "BIN\0"u8.CopyTo(bin[4..]);
            stream.Write(bin);
        }
        stream.SetLength(size);
        stream.Position = 0;
        return stream;
    }
}
