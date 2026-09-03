namespace ApexRacers.Core;

/// <summary>Canonicalizes the upstream spellings of an absent track Configuration Name.</summary>
public static class ConfigurationName
{
    /// <summary>
    /// Returns the persistence representation: the empty string when iRacing omits the value,
    /// supplies only whitespace, or sends its results-payload <c>N/A</c> sentinel.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Equals(value.Trim(), "N/A", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : value;
    }

    /// <summary>Returns null when <paramref name="value"/> carries no Configuration Name.</summary>
    public static string? NullIfAbsent(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }
}
