using Resource.Domain.Files;

namespace Resource.UnitTests.Inventory;

public sealed class AttachmentRecoveryTests
{
    [Fact]
    public void Retry_identity_is_stable_and_separate_for_each_record()
    {
        var file=Guid.NewGuid();var purchase=Guid.NewGuid();
        AttachmentIntent.Identity(file,purchase).Should().Be(AttachmentIntent.Identity(file,purchase));
        AttachmentIntent.Identity(file,purchase).Should().NotBe(AttachmentIntent.Identity(file,Guid.NewGuid()));
    }
    [Theory]
    [InlineData(true,"FINALIZE")][InlineData(false,"RELEASE")]
    public void Authoritative_committed_reference_decides_recovery_even_when_journal_says_reserving(bool committed,string expected)
    {
        var intent=new AttachmentIntent {State="RESERVING",CreatedAt=DateTimeOffset.UtcNow.AddDays(-1)};
        intent.RecoveryAction(committed).Should().Be(expected);
    }
}
