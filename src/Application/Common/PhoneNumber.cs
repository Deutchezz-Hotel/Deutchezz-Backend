using System.Text;

namespace Deutchezz.Application.Common;

/// <summary>
/// Normalizes user-entered telephone numbers to a single canonical form so equality and the
/// unique index behave consistently (BR-027). Nigerian local numbers (<c>0XXXXXXXXXX</c>) and
/// the bare country-code form (<c>234XXXXXXXXXX</c>) both become <c>+234XXXXXXXXXX</c>.
/// </summary>
public static class PhoneNumber
{
    /// <summary>
    /// Attempts to normalize <paramref name="input"/>. A null/blank/whitespace input normalizes to
    /// <c>null</c> (treated as "no phone number") and succeeds. Returns <c>false</c> when the input
    /// is present but not a valid number.
    /// </summary>
    public static bool TryNormalize(string? input, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(input))
            return true;

        var cleaned = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (char.IsDigit(c) || c == '+')
                cleaned.Append(c);
        }

        if (cleaned.Length == 0)
            return true;

        var s = cleaned.ToString();
        string digits;
        if (s.StartsWith('+'))
            digits = s[1..];
        else if (s.StartsWith("00", StringComparison.Ordinal))
            digits = s[2..];
        else if (s.StartsWith('0'))
            digits = "234" + s[1..];
        else
            digits = s;

        if (!digits.All(char.IsDigit))
            return false;
        if (digits.Length < 8 || digits.Length > 15)
            return false;

        normalized = "+" + digits;
        return true;
    }
}
