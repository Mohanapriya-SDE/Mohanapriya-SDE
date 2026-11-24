using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KredFlow.Application.Common;

/// <summary>Fingerprints a request so an idempotency key reused for a different request can be detected.</summary>
public static class RequestFingerprint
{
    public static string Compute(params object?[] parts)
    {
        var text = string.Join('|', parts.Select(p => p switch
        {
            null => string.Empty,
            decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => p.ToString()
        }));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
