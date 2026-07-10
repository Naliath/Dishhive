namespace Dishhive.Api.Models;

/// <summary>
/// Application-wide policy for enums represented as plain strings outside typed EF
/// properties and JSON DTOs. Writes use the canonical CLR member name; reads accept
/// casing differences but reject numeric and undefined values.
/// </summary>
public static class EnumNames
{
    public static string ToName<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Undefined enum value");
        }

        return value.ToString();
    }

    public static bool TryParse<TEnum>(string? name, out TEnum value) where TEnum : struct, Enum
    {
        value = default;
        return !string.IsNullOrWhiteSpace(name)
            && !long.TryParse(name, out _)
            && Enum.TryParse(name.Trim(), ignoreCase: true, out value)
            && Enum.IsDefined(value);
    }

    public static TEnum Parse<TEnum>(string name) where TEnum : struct, Enum
    {
        return TryParse<TEnum>(name, out var value)
            ? value
            : throw new FormatException($"'{name}' is not a defined {typeof(TEnum).Name} value.");
    }
}
