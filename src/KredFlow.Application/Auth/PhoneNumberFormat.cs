using System.Text.RegularExpressions;

namespace KredFlow.Application.Auth;

public static partial class PhoneNumberFormat
{
    [GeneratedRegex(@"^(?:\+91)?([6-9]\d{9})$")]
    private static partial Regex IndianMobile();

    /// <summary>Accepts an Indian mobile number with an optional +91 and returns it as +91XXXXXXXXXX.</summary>
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        var match = IndianMobile().Match(value?.Trim() ?? string.Empty);
        if (!match.Success)
        {
            return false;
        }

        normalized = "+91" + match.Groups[1].Value;
        return true;
    }
}
