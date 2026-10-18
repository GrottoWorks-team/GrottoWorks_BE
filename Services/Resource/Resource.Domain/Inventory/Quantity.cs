namespace Resource.Domain.Inventory;

/// <summary>Quantities are <c>decimal(12,2)</c> in the ERD, expressed in the material's unit.</summary>
public static class Quantity
{
    public const decimal Max = 9_999_999_999.99m;

    /// <summary>Validates a strictly positive quantity with at most two decimals.</summary>
    public static decimal Positive(decimal value, string code, string field = "Quantity")
    {
        if (value <= 0 || value > Max || decimal.Round(value, 2) != value)
        {
            throw DomainRuleException.Validation(
                code,
                $"{field} must be greater than 0, at most {Max} and have at most 2 decimals.");
        }

        return value;
    }
}
