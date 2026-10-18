using Resource.Domain;
using Resource.Domain.Inventory;

namespace Resource.UnitTests.Inventory;

/// <summary>BA 8.5 / API Contract 7.1 formulas — table-driven (plan.md §4.2 "InventoryCalculator + bảng test").</summary>
public sealed class InventoryCalculatorTests
{
    [Theory]
    //          required  ledger  allocated | available unallocated shortage
    [InlineData(100,       0,      0,          0,        0,          100)]  // nothing in stock
    [InlineData(100,       40,     0,          40,       40,         60)]   // partial stock
    [InlineData(100,       100,    0,          100,      100,        0)]    // exactly covered
    [InlineData(100,       130,    0,          130,      130,        0)]    // surplus: shortage never negative (BR-25)
    [InlineData(0,         25,     0,          25,       25,         0)]    // stock without requirement
    [InlineData(50,        30,     20,         30,       10,         20)]   // allocation reduces unallocated, not available
    [InlineData(50,        30,     30,         30,       0,          20)]   // fully allocated
    [InlineData(10,        -5,     0,          0,        0,          10)]   // defensive clamp of a broken ledger
    public void Calculates_available_unallocated_and_shortage(
        decimal required, decimal ledger, decimal allocated,
        decimal expectedAvailable, decimal expectedUnallocated, decimal expectedShortage)
    {
        var figures = InventoryCalculator.Calculate(required, ledger, allocated);

        figures.ConfirmedAvailable.Should().Be(expectedAvailable);
        figures.Unallocated.Should().Be(expectedUnallocated);
        figures.Shortage.Should().Be(expectedShortage);
        figures.Required.Should().Be(required);
        figures.Allocated.Should().Be(allocated);
    }

    [Fact]
    public void Donation_scenario_from_BA_edge_case_17_counts_only_usable()
    {
        // MATERIAL donation of 15 with 3 unusable → only 12 are posted (DONATION_RECEIPT +12).
        var ledger = new[]
        {
            MaterialTransaction.Post(Guid.NewGuid(), Guid.NewGuid(), TransactionType.OpeningStock, 5, DateTimeOffset.UtcNow, null, null),
            MaterialTransaction.Post(Guid.NewGuid(), Guid.NewGuid(), TransactionType.DonationReceipt, 12, DateTimeOffset.UtcNow, null, null,
                TransactionReferenceType.DonationReceiptItem, Guid.NewGuid())
        };

        InventoryCalculator.Calculate(20, ledger.Sum(t => t.Quantity)).Shortage.Should().Be(3);
    }

    [Theory]
    [InlineData(TransactionType.OpeningStock, 1)]
    [InlineData(TransactionType.PurchaseDelivery, 1)]
    [InlineData(TransactionType.DonationReceipt, 1)]
    [InlineData(TransactionType.TaskReturn, 1)]
    [InlineData(TransactionType.TransferIn, 1)]
    [InlineData(TransactionType.TransferOut, -1)]
    [InlineData(TransactionType.AdjustmentDamaged, -1)]
    [InlineData(TransactionType.AdjustmentLost, -1)]
    [InlineData(TransactionType.AdjustmentDisposed, -1)]
    public void Ledger_sign_is_fixed_by_type(TransactionType type, int sign)
    {
        var reference = type == TransactionType.OpeningStock ? (TransactionReferenceType?)null : TransactionReferenceType.InventoryAdjustment;
        var transaction = MaterialTransaction.Post(Guid.NewGuid(), Guid.NewGuid(), type, 7.5m, DateTimeOffset.UtcNow, null, null,
            reference, reference is null ? null : Guid.NewGuid());

        transaction.Quantity.Should().Be(7.5m * sign);
    }

    [Fact]
    public void Movements_other_than_opening_stock_need_a_source_reference()
    {
        var act = () => MaterialTransaction.Post(Guid.NewGuid(), Guid.NewGuid(), TransactionType.PurchaseDelivery, 1, DateTimeOffset.UtcNow, null, null);

        act.Should().Throw<DomainRuleException>().Which.Code.Should().Be("TRANSACTION_REFERENCE_REQUIRED");
    }

    [Fact]
    public void Quantity_must_be_positive()
    {
        var act = () => MaterialTransaction.Post(Guid.NewGuid(), Guid.NewGuid(), TransactionType.OpeningStock, 0, DateTimeOffset.UtcNow, null, null);

        act.Should().Throw<DomainRuleException>().Which.Code.Should().Be("TRANSACTION_QUANTITY_INVALID");
    }
}
