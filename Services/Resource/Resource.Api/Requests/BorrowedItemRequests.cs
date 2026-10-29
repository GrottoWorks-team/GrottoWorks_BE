using System.ComponentModel.DataAnnotations;
using Resource.Domain.Contributions;

namespace Resource.Api.Requests;

/// <summary><c>LoanOfferRequest</c> (F-RES-18, D-05): lender details may be entered on the lender's behalf.</summary>
public sealed class LoanOfferRequest
{
    [Required(ErrorMessage = "Season is required.")]
    public Guid? SeasonId { get; init; }

    public Guid? WorkAreaId { get; init; }

    [Required(ErrorMessage = "Lender name is required.")]
    [StringLength(150, ErrorMessage = "Lender name must not exceed 150 characters.")]
    public string? OwnerName { get; init; }

    [StringLength(30, ErrorMessage = "Lender phone must not exceed 30 characters.")]
    public string? OwnerPhone { get; init; }

    [StringLength(255, ErrorMessage = "Lender address must not exceed 255 characters.")]
    public string? OwnerAddress { get; init; }

    [StringLength(2000, ErrorMessage = "Lender note must not exceed 2000 characters.")]
    public string? OwnerNote { get; init; }

    [Required(ErrorMessage = "Item name is required.")]
    [StringLength(150, ErrorMessage = "Item name must not exceed 150 characters.")]
    public string? ItemName { get; init; }

    [Required(ErrorMessage = "Quantity is required.")]
    public decimal? Quantity { get; init; }

    [StringLength(2000, ErrorMessage = "Description must not exceed 2000 characters.")]
    public string? Description { get; init; }

    [StringLength(50, ErrorMessage = "Initial condition must not exceed 50 characters.")]
    public string? InitialCondition { get; init; }

    [Required(ErrorMessage = "Expected return date is required.")]
    public DateOnly? ExpectedReturnDate { get; init; }
}

/// <summary><c>AcceptLoanOfferRequest</c>: optional receiving work area and corrected return date.</summary>
public sealed class AcceptLoanOfferRequest
{
    public long? Version { get; init; }

    public Guid? WorkAreaId { get; init; }

    public DateOnly? ExpectedReturnDate { get; init; }
}

/// <summary><c>IssueBorrowedItemRequest</c>: the date the parish received it (default today).</summary>
public sealed class IssueBorrowedItemRequest
{
    public long? Version { get; init; }

    public DateOnly? BorrowDate { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}

/// <summary><c>BorrowedItemAssignmentRequest</c>: work area (and optionally one of its tasks) using the item.</summary>
public sealed class BorrowedItemAssignmentRequest
{
    public long? Version { get; init; }

    [Required(ErrorMessage = "Work area is required.")]
    public Guid? WorkAreaId { get; init; }

    public Guid? TaskId { get; init; }
}

/// <summary><c>ReturnBorrowedItemRequest</c>: return date and condition are recorded (BR-42).</summary>
public sealed class ReturnBorrowedItemRequest
{
    public long? Version { get; init; }

    public DateOnly? ReturnDate { get; init; }

    [Required(ErrorMessage = "Return condition is required.")]
    public ReturnCondition? Condition { get; init; }

    public decimal? ReturnedQuantity { get; init; }

    [StringLength(2000, ErrorMessage = "Note must not exceed 2000 characters.")]
    public string? Note { get; init; }
}

/// <summary><c>BorrowedItemIncidentRequest</c>: damage or loss while the item is out.</summary>
public sealed class BorrowedItemIncidentRequest
{
    [Required(ErrorMessage = "Condition is required.")]
    public ReturnCondition? Condition { get; init; }

    public decimal? Quantity { get; init; }

    public DateOnly? Date { get; init; }

    [Required(ErrorMessage = "Description is required.")]
    [StringLength(2000, ErrorMessage = "Description must not exceed 2000 characters.")]
    public string? Description { get; init; }
}
