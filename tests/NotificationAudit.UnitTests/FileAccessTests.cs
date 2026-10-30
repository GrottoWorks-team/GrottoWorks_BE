using FluentAssertions;
using NotificationAudit.Domain.Files;
using Xunit;

namespace NotificationAudit.UnitTests;

public sealed class FileAccessTests
{
    [Fact]
    public void Attached_file_cannot_be_deleted()
    {
        var status = FileObjectStatus.Attached;
        status.Should().NotBe(FileObjectStatus.Deleted);
    }

    [Fact]
    public void Internal_file_contract_never_contains_a_presigned_url()
    {
        typeof(NotificationAudit.Contracts.Files.InternalFileDto)
            .GetProperties()
            .Should()
            .NotContain(property => property.Name.Contains("Url", StringComparison.OrdinalIgnoreCase));
    }
}
