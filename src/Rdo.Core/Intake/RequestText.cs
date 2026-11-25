using System.Globalization;
using System.Text.RegularExpressions;

namespace Rdo.Core.Intake;

/// <summary>Deterministic readers for the parts of a request code can check on its own.</summary>
public static partial class RequestText
{
    [GeneratedRegex(@"\bFM[\s\-]?(\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex FormulationRegex();

    [GeneratedRegex(@"\b(?:KW|CW|week|Woche)\s*(\d{1,2})(?:\s*/\s*(\d{4}))?\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeekRegex();

    [GeneratedRegex(@"\b(\d{4})-(\d{2})-(\d{2})\b")]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(@"\b(\d{1,2})\.(\d{1,2})\.(\d{4})?")]
    private static partial Regex GermanDateRegex();

    [GeneratedRegex(@"\b(\d{1,2})\s*(?:Muster|Probe|Proben|Sätze|Satz|samples?|sets?|pad sets?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SamplesRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();

    public static string Normalize(string text) => SpaceRegex().Replace(text, " ").Trim().ToLowerInvariant();

    public static bool Contains(string haystack, string? needle) =>
        !string.IsNullOrWhiteSpace(needle) && Normalize(haystack).Contains(Normalize(needle), StringComparison.Ordinal);

    public static IReadOnlyList<string> FormulationCodes(string text) =>
        FormulationRegex().Matches(text).Select(m => $"FM-{m.Groups[1].Value}").Distinct().ToList();

    public static int? Samples(string text)
    {
        var m = SamplesRegex().Match(text);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// A deadline written as a calendar week ("KW 14", "week 14") means the Friday of that ISO week;
    /// a date means that date. A week already past this year means next year.
    /// </summary>
    public static DateOnly? Deadline(string text, DateOnly today)
    {
        var iso = IsoDateRegex().Match(text);
        if (iso.Success)
        {
            return new DateOnly(Int(iso, 1), Int(iso, 2), Int(iso, 3));
        }

        var week = WeekRegex().Match(text);
        if (week.Success)
        {
            var w = Int(week, 1);
            var year = week.Groups[2].Success ? Int(week, 2) : today.Year;
            if (w < 1 || w > ISOWeek.GetWeeksInYear(year))
            {
                return null;
            }

            var friday = DateOnly.FromDateTime(ISOWeek.ToDateTime(year, w, DayOfWeek.Friday));
            if (!week.Groups[2].Success && friday < today)
            {
                friday = DateOnly.FromDateTime(ISOWeek.ToDateTime(year + 1, Math.Min(w, ISOWeek.GetWeeksInYear(year + 1)), DayOfWeek.Friday));
            }

            return friday;
        }

        var de = GermanDateRegex().Match(text);
        if (de.Success)
        {
            var day = Int(de, 1);
            var month = Int(de, 2);
            if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(today.Year, month))
            {
                return null;
            }

            var year = de.Groups[3].Success ? Int(de, 3) : today.Year;
            var date = new DateOnly(year, month, day);
            return !de.Groups[3].Success && date < today ? date.AddYears(1) : date;
        }

        return null;
    }

    private static readonly char[] Separators = [' ', ',', '.', ';', ':', '(', ')', '/', '-'];

    private static readonly string[] FrontWords = ["front", "vorderachse", "va", "vorne"];
    private static readonly string[] RearWords = ["rear", "hinterachse", "ha", "hinten"];

    /// <summary>"front", "rear" or null when the words do not say.</summary>
    public static string? Axle(string text)
    {
        var words = Normalize(text).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var front = words.Any(w => FrontWords.Contains(w));
        var rear = words.Any(w => RearWords.Contains(w));
        return front == rear ? null : front ? "front" : "rear";
    }

    private static int Int(Match m, int group) => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);
}
