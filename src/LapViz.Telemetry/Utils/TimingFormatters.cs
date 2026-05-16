using System;
using System.Globalization;
#if !NETSTANDARD2_0
using System.Linq;
#endif

namespace LapViz.Telemetry.Utils;

/// <summary>
/// Formatting and parsing helpers for displaying and interpreting TimeSpan timing values.
/// All formatting logic lives here — models carry raw TimeSpan values.
/// </summary>
public static class TimingFormatters
{
    /// <summary>
    /// Formats a lap or sector time for display.
    /// Examples: "48.749", "1:23.456", null → ""
    /// </summary>
    public static string FormatLapTime(TimeSpan? time)
    {
        if (!time.HasValue) return "";

        var totalSeconds = time.Value.TotalSeconds;
        if (totalSeconds < 0) return "";

        if (time.Value.TotalMinutes >= 1)
        {
            var minutes = (int)time.Value.TotalMinutes;
            var seconds = time.Value.TotalSeconds - (minutes * 60);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00.000}", minutes, seconds);
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.000}", totalSeconds);
    }

    /// <summary>
    /// Formats a lap or sector time for display, optionally with a sign prefix.
    /// Examples: "+48.749", "-1:23.456", null → defaultValue
    /// </summary>
    public static string FormatLapTime(TimeSpan? time, string defaultValue, bool addSign = false)
    {
        if (!time.HasValue) return defaultValue;

        var formatted = FormatLapTime(time);
        if (string.IsNullOrEmpty(formatted)) return defaultValue;

        if (addSign)
        {
            if (time.Value < TimeSpan.Zero)
                formatted = "-" + formatted;
            else if (time.Value > TimeSpan.Zero)
                formatted = "+" + formatted;
        }

        return formatted;
    }

    /// <summary>
    /// Formats a session elapsed time or countdown for display.
    /// Examples: "12:34", "1:23:45", null → ""
    /// </summary>
    public static string FormatSessionTime(TimeSpan? time)
    {
        if (!time.HasValue) return "";

        return time.Value.TotalHours >= 1
            ? time.Value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.Value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats an array of sector times for display, returning "-" for null entries.
    /// </summary>
    public static string FormatSectorTime(TimeSpan?[] sectorTimes, int sectorIndex)
    {
        if (sectorTimes == null || sectorTimes.Length <= sectorIndex || !sectorTimes[sectorIndex].HasValue)
            return "-";

        return FormatLapTime(sectorTimes[sectorIndex]);
    }

    /// <summary>
    /// Parses a timing string (e.g. "48.749", "1:23.456") into a TimeSpan.
    /// Handles both comma and dot decimal separators.
    /// </summary>
    public static TimeSpan? ParseLapTime(string timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr)) return null;

        // Sentinel values from timing sources that indicate no valid time
        if (timeStr.Equals("-", StringComparison.Ordinal) ||
            StringEqualsIgnoreCase(timeStr, "No Time") ||
            StringEqualsIgnoreCase(timeStr, "N/A") ||
            StringEqualsIgnoreCase(timeStr, "DNS") ||
            StringEqualsIgnoreCase(timeStr, "DNF") ||
            StringEqualsIgnoreCase(timeStr, "DSQ") ||
            StringEqualsIgnoreCase(timeStr, "PIT"))
            return null;

        var normalized = timeStr.Replace(',', '.');
        var parts = normalized.Split(':');

        if (parts.Length == 2)
        {
            if (int.TryParse(parts[0], out var mins) &&
                double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var secs))
            {
                return TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs);
            }
        }

        if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var totalSecs))
        {
            return TimeSpan.FromSeconds(totalSecs);
        }

        return null;
    }

    /// <summary>
    /// Parses a millisecond string (e.g. "48749") into a TimeSpan.
    /// </summary>
    public static TimeSpan? ParseMilliseconds(string msString)
    {
        if (string.IsNullOrEmpty(msString)) return null;
        if (!long.TryParse(msString, out var ms)) return null;
        return TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>
    /// Parses a time-of-day string (e.g. "14:23:45", "9:05") into a DateTimeOffset
    /// using today's date and the local offset.
    /// </summary>
    public static DateTimeOffset? ParseTimeOfDay(string timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr)) return null;

        string[] formats = { "H:mm:ss", "HH:mm:ss", "H:mm", "HH:mm" };
        if (TimeSpan.TryParseExact(timeStr.Trim(), formats, CultureInfo.InvariantCulture, out var tod))
        {
            var now = DateTimeOffset.Now;
            return new DateTimeOffset(now.Date + tod, now.Offset);
        }

        return null;
    }

    /// <summary>
    /// Formats a DateTimeOffset as a time-of-day string ("HH:mm:ss").
    /// </summary>
    public static string FormatTimeOfDay(DateTimeOffset? time)
    {
        if (!time.HasValue) return "";
        return time.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses a track length string (e.g. "1.594 km", "3.450", "1594 m") into kilometers.
    /// </summary>
    public static double ParseTrackLength(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;

        var normalized = value.Trim()
            .Replace(",", ".");

        // Strip known unit suffixes
        bool hasKm = normalized.IndexOf("km", StringComparison.OrdinalIgnoreCase) >= 0;
        bool hasMi = normalized.IndexOf("mi", StringComparison.OrdinalIgnoreCase) >= 0;

        normalized = StringReplaceIgnoreCase(normalized, "km", "");
        normalized = StringReplaceIgnoreCase(normalized, "mi", "");
        normalized = normalized.Trim();

        // Detect meters: has 'm' but not 'km'/'mi'
        bool isMeters = !hasKm && !hasMi
            && value.IndexOf("m", StringComparison.OrdinalIgnoreCase) >= 0;

        if (isMeters)
            normalized = StringReplaceIgnoreCase(normalized, "m", "").Trim();

        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return isMeters ? result / 1000.0 : result;
        }

        return 0;
    }

    /// <summary>
    /// Parses a session time string (e.g. "12:34", "1:23:45") into a TimeSpan.
    /// Also handles plain seconds and minute:seconds.milliseconds formats.
    /// </summary>
    public static TimeSpan? ParseSessionTime(string timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr)) return null;

        var normalized = timeStr.Trim().Replace(',', '.');
        var parts = normalized.Split(':');

        if (parts.Length == 3)
        {
            if (int.TryParse(parts[0], out var hours) &&
                int.TryParse(parts[1], out var mins) &&
                double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var secs))
            {
                return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs);
            }
        }

        if (parts.Length == 2)
        {
            if (int.TryParse(parts[0], out var mins) &&
                double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var secs))
            {
                return TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs);
            }
        }

        if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var totalSecs))
        {
            return TimeSpan.FromSeconds(totalSecs);
        }

        return null;
    }

    /// <summary>
    /// Formats a track length in km for display (e.g. 1.594 → "1.594 km").
    /// Returns empty string if the value is 0.
    /// </summary>
    public static string FormatTrackLength(double km)
    {
        if (km <= 0) return "";
        return string.Format(CultureInfo.InvariantCulture, "{0:0.000} km", km);
    }

    private static bool StringEqualsIgnoreCase(string a, string b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string StringReplaceIgnoreCase(string source, string oldValue, string newValue)
    {
#if NETSTANDARD2_0
        // netstandard2.0 doesn't have string.Replace with StringComparison overload
        int index;
        while ((index = source.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            source = source.Substring(0, index) + newValue + source.Substring(index + oldValue.Length);
        }

        return source;
#else
        return source.Replace(oldValue, newValue, StringComparison.OrdinalIgnoreCase);
#endif
    }
}
