using System.Text.RegularExpressions;

namespace Reign.Mcp.Server;

internal static partial class InputGuard
{
    [GeneratedRegex(@"^[A-Za-z0-9_.:\-]{1,160}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    public static string OptionalIdentifier(string? value, string name)
    {
        var result = value?.Trim() ?? string.Empty;
        if (result.Length > 0 && !IdentifierPattern().IsMatch(result))
        {
            throw new ArgumentException(
                $"{name} must contain only letters, numbers, underscore, hyphen, period, or colon and be at most 160 characters.",
                name);
        }
        return result;
    }

    public static string BoundedText(string? value, string name, int maximumLength)
    {
        var result = value?.Trim() ?? string.Empty;
        if (result.Length > maximumLength)
        {
            throw new ArgumentException($"{name} must be at most {maximumLength} characters.", name);
        }
        return result;
    }

    public static int Range(int value, string name, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be between {minimum} and {maximum}.");
        }
        return value;
    }

    public static void RequireConfirmation(string actual, string expected)
    {
        if (!string.Equals(actual?.Trim(), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"This operation requires the exact confirmation text: {expected}");
        }
    }
}
