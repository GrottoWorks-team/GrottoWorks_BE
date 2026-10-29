using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resource.Domain.Contributions;

namespace Resource.Infrastructure.Data.Configurations;

/// <summary><c>borrowed_item</c> (ERD v3.6.2, D-05) + extension columns <c>rejection_reason</c>, <c>last_reminded_on</c>.</summary>
public sealed class BorrowedItemConfiguration : IEntityTypeConfiguration<BorrowedItem>
{
    public void Configure(EntityTypeBuilder<BorrowedItem> builder)
    {
        builder.ToTable("borrowed_item", table =>
        {
            table.HasCheckConstraint("borrowed_item_positive_quantity", "\"borrowed_item_quantity\" > 0");
            table.HasCheckConstraint("borrowed_item_rejection_reason", "\"borrowed_item_status\" <> 'REJECTED' OR \"rejection_reason\" IS NOT NULL");
        });
        builder.HasKey(item => item.BorrowedItemId);
        builder.Property(item => item.BorrowedItemId).HasColumnName("borrowed_item_id").ValueGeneratedNever();
        // user, work area, task and season ids reference other services: no FK (spec 2.1).
        builder.Property(item => item.OfferedByUserId).HasColumnName("offered_by_user_id").IsRequired();
        builder.Property(item => item.AcceptedByUserId).HasColumnName("accepted_by_user_id");
        builder.Property(item => item.WorkAreaId).HasColumnName("work_area_id");
        builder.Property(item => item.TaskId).HasColumnName("task_id");
        builder.Property(item => item.SeasonId).HasColumnName("season_id").IsRequired();
        builder.Property(item => item.OwnerName).HasColumnName("owner_name").HasMaxLength(BorrowedItem.MaxNameLength).IsRequired();
        builder.Property(item => item.OwnerPhone).HasColumnName("owner_phone").HasMaxLength(BorrowedItem.MaxPhoneLength);
        builder.Property(item => item.OwnerAddress).HasColumnName("owner_address").HasMaxLength(BorrowedItem.MaxAddressLength);
        builder.Property(item => item.OwnerNote).HasColumnName("owner_note");
        builder.Property(item => item.ItemName).HasColumnName("borrowed_item_name").HasMaxLength(BorrowedItem.MaxNameLength).IsRequired();
        builder.Property(item => item.Quantity).HasColumnName("borrowed_item_quantity").HasPrecision(12, 2).IsRequired();
        builder.Property(item => item.Description).HasColumnName("borrowed_item_description");
        builder.Property(item => item.InitialCondition).HasColumnName("initial_condition").HasMaxLength(BorrowedItem.MaxConditionLength);
        builder.Property(item => item.ExpectedReturnDate).HasColumnName("expected_return_date").HasColumnType("date").IsRequired();
        builder.Property(item => item.Status).HasColumnName("borrowed_item_status").HasConversion(EnumText.Converter<BorrowedItemStatus>()).HasMaxLength(30).IsRequired();
        builder.Property(item => item.AcceptedAt).HasColumnName("accepted_at").HasColumnType("timestamp with time zone");
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(item => item.RejectionReason).HasColumnName("rejection_reason");
        builder.Property(item => item.LastRemindedOn).HasColumnName("last_reminded_on").HasColumnType("date");
        builder.Property(item => item.Version).IsRowVersion();

        builder.HasIndex(item => new { item.SeasonId, item.Status, item.ExpectedReturnDate }, "ix_borrowed_item_season_status_due");
        builder.HasMany(item => item.Transactions).WithOne().HasForeignKey(transaction => transaction.BorrowedItemId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(item => item.Transactions).HasField("_transactions").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>borrow_transaction</c> (ERD v3.6.2): issue (closed by the return, BR-42) and incident rows.</summary>
public sealed class BorrowTransactionConfiguration : IEntityTypeConfiguration<BorrowTransaction>
{
    public void Configure(EntityTypeBuilder<BorrowTransaction> builder)
    {
        builder.ToTable("borrow_transaction", table =>
            table.HasCheckConstraint("borrow_transaction_return_condition", "\"return_date\" IS NULL OR \"return_condition\" IS NOT NULL"));
        builder.HasKey(transaction => transaction.BorrowTransactionId);
        builder.Property(transaction => transaction.BorrowTransactionId).HasColumnName("borrow_transaction_id").ValueGeneratedNever();
        builder.Property(transaction => transaction.BorrowedItemId).HasColumnName("borrowed_item_id").IsRequired();
        builder.Property(transaction => transaction.BorrowDate).HasColumnName("borrow_date").HasColumnType("date").IsRequired();
        builder.Property(transaction => transaction.ReturnDate).HasColumnName("return_date").HasColumnType("date");
        builder.Property(transaction => transaction.ReturnCondition).HasColumnName("return_condition").HasConversion(EnumText.NullableConverter<ReturnCondition>()).HasMaxLength(50);
        builder.Property(transaction => transaction.ReturnedQuantity).HasColumnName("returned_quantity").HasPrecision(12, 2);
        builder.Property(transaction => transaction.RecordedByUserId).HasColumnName("recorded_by_user_id");
        builder.Property(transaction => transaction.Note).HasColumnName("transaction_note");
    }
}
