using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Reporting.Infrastructure.Data;

/// <summary>Stores enums as SNAKE_CASE_UPPER text (e.g. IN_PROGRESS, OPENING_STOCK), matching API values.</summary>
internal static class EnumText
{
    public static ValueConverter<TEnum, string> Converter<TEnum>()
        where TEnum : struct, Enum =>
        new(value => ToText(value), text => FromText<TEnum>(text));

    public static ValueConverter<TEnum?, string?> NullableConverter<TEnum>()
        where TEnum : struct, Enum =>
        new(value => value == null ? null : ToText(value.Value), text => text == null ? null : FromText<TEnum>(text));

    public static string ToText<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (var index = 0; index < name.Length; index++)
        {
            if (index > 0 && char.IsUpper(name[index]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(name[index]));
        }

        return builder.ToString();
    }

    public static TEnum FromText<TEnum>(string text)
        where TEnum : struct, Enum =>
        Enum.Parse<TEnum>(text.Replace("_", string.Empty), ignoreCase: true);
}
