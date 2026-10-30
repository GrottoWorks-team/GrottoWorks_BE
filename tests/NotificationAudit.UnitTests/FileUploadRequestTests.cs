using FluentAssertions;
using NotificationAudit.Application.Files;
using NotificationAudit.Contracts.Files;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class FileUploadRequestTests
{
    [Theory]
    [InlineData(FilePurposeDto.ProgressEvidence, "image/jpeg", 10 * 1024 * 1024)]
    [InlineData(FilePurposeDto.ReadinessEvidence, "image/webp", 1)]
    [InlineData(FilePurposeDto.PurchaseReceipt, "application/pdf", 15 * 1024 * 1024)]
    public void Allowed_file_combinations_are_accepted(
        FilePurposeDto purpose,
        string contentType,
        long sizeBytes)
    {
        var action = () => CreateUploadRequestHandler.ValidateFile(purpose, contentType, sizeBytes, false);
        action.Should().NotThrow();
    }

    [Fact]
    public void Other_pdf_is_rejected_until_explicitly_enabled()
    {
        var action = () => CreateUploadRequestHandler.ValidateFile(
            FilePurposeDto.Other,
            "application/pdf",
            1024,
            false);
        action.Should().Throw<Exception>();
    }
}
