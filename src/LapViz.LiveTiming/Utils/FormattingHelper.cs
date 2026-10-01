using System.Globalization;

namespace LapViz.LiveTiming.Utils;

public static class FormattingHelper
{
    public static string GetFormattedTime(TimeSpan? time, string defaultValue = "-", bool addSign = false)
    {
        if (time.HasValue)
        {
            var formattedTime = string.Empty;

            if (time.Value.TotalSeconds >= 60)
            {
                formattedTime = time.Value.ToString("m\\:ss\\.fff", CultureInfo.InvariantCulture);
            }
            else if (time.Value.TotalSeconds >= 10)
            {
                formattedTime = time.Value.ToString("ss\\.fff", CultureInfo.InvariantCulture);
            }
            else
            {
                formattedTime = time.Value.ToString("s\\.fff", CultureInfo.InvariantCulture);
            }

            if (addSign)
            {
                if (time.Value < TimeSpan.Zero)
                {
                    formattedTime = "-" + formattedTime;
                }
                else if (time.Value > TimeSpan.Zero)
                {
                    formattedTime = "+" + formattedTime;
                }
            }

            return formattedTime;
        }
        return defaultValue;
    }
}
