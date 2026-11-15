using KredFlow.Domain.Entities;

namespace KredFlow.Application.Lenders;

public class LenderOptions
{
    public const string SectionName = "Lenders";

    public string[] SupportedTimeZones { get; set; } = [];

    /// <summary>The list must be non-empty, resolvable on this machine, and include the registration default.</summary>
    public bool IsValid()
    {
        if (SupportedTimeZones.Length == 0 || !SupportedTimeZones.Contains(Lender.DefaultTimeZone, StringComparer.Ordinal))
        {
            return false;
        }

        try
        {
            foreach (var id in SupportedTimeZones)
            {
                TimeZoneInfo.FindSystemTimeZoneById(id);
            }

            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}
