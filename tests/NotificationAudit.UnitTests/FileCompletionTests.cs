using FluentAssertions;
using NotificationAudit.Infrastructure.Files;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class FileCompletionTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0x00 }, "image/jpeg")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, "application/pdf")]
    public void Content_type_is_detected_from_file_signature(byte[] content, string expected) =>
        MinioFileObjectStorage.DetectContentType(content).Should().Be(expected);

    [Fact]
    public void Arbitrary_content_is_not_treated_as_a_supported_file() =>
        MinioFileObjectStorage.DetectContentType("not-a-file"u8).Should().Be("application/octet-stream");
}
