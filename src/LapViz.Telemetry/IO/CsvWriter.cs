using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.IO;

/// <summary>
/// The samples of a session in a table (CSV, for a spreadsheet, MATLAB, pandas...): one row per sample with its time from
/// the start of the session, its lap and its time in the lap, then a column per channel. Invariant culture (decimal point,
/// comma between the columns), an empty cell for a missing value; lap 0 is before the first lap (or between two).
/// </summary>
[Export(typeof(ITelemetryDataWriter))]
[PartCreationPolicy(CreationPolicy.NonShared)]
public class CsvWriter : ITelemetryDataWriter
{
    /// <summary>Columns before the channels.</summary>
    public static readonly IReadOnlyList<string> Columns = new[] { "Time (s)", "Lap", "Lap time (s)" };

    /// <summary>Writes the session to <paramref name="output"/> (kept when it exists and <paramref name="overwrite"/> is false).</summary>
    public void WriteAll(DeviceSessionData driverSessionData, string output, bool overwrite)
    {
        if (!overwrite && File.Exists(output))
        {
            return;
        }

        using var writer = new StreamWriter(output, false, new UTF8Encoding(false));
        Write(driverSessionData, writer);
    }

    /// <summary>The session as CSV (UTF-8 without BOM), at the start of the stream.</summary>
    public MemoryStream WriteAll(DeviceSessionData driverSessionData)
    {
        var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536, leaveOpen: true))
        {
            Write(driverSessionData, writer);
        }

        stream.Position = 0;
        return stream;
    }

    public void WriteData(ITelemetryData geoTelemetryData)
    {
        throw new NotImplementedException();
    }

    public void WriteEvent(SessionDataEvent sessionDataEvent)
    {
        throw new NotImplementedException();
    }

    private static void Write(DeviceSessionData session, TextWriter writer)
    {
        var channels = session.TelemetryChannels ?? new List<string>();
        writer.WriteLine(string.Join(",", Columns.Concat(channels).Select(Escape)));

        var samples = session.TelemetryData.OrderBy(x => x.Timestamp).ToList();
        if (samples.Count == 0)
        {
            return;
        }

        // The laps in their order, each one [start, end]
        var laps = session.Events
            .Where(x => x.Type == SessionEventType.Lap && x.Time > TimeSpan.Zero)
            .Select(x => (Number: x.LapNumber, Start: x.Timestamp.ToUniversalTime() - x.Time, End: x.Timestamp.ToUniversalTime()))
            .OrderBy(x => x.Start)
            .ToList();

        var origin = samples[0].Timestamp.ToUniversalTime();
        var lap = 0;
        var cells = new string[Columns.Count + channels.Count];
        foreach (var sample in samples)
        {
            var timestamp = sample.Timestamp.ToUniversalTime();
            while (lap < laps.Count && timestamp > laps[lap].End)
            {
                lap++;
            }

            var inLap = lap < laps.Count && timestamp > laps[lap].Start;
            cells[0] = Number((timestamp - origin).TotalSeconds);
            cells[1] = inLap ? laps[lap].Number.ToString(CultureInfo.InvariantCulture) : "0";
            cells[2] = inLap ? Number((timestamp - laps[lap].Start).TotalSeconds) : string.Empty;
            for (int c = 0; c < channels.Count; c++)
            {
                var value = sample.Data != null && c < sample.Data.Count ? sample.Data[c] : null;
                cells[Columns.Count + c] = value is double v && !double.IsNaN(v) ? Number(v) : string.Empty;
            }

            writer.WriteLine(string.Join(",", cells));
        }
    }

    /// <summary>The shortest text that reads back the value: a channel stored in float32 is written as such (11.81).</summary>
    private static string Number(double value)
    {
        var single = (float)value;
        return single == value
            ? single.ToString("R", CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>A name between quotes when it holds a comma, a quote or a line break.</summary>
    private static string Escape(string text) =>
        text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
}
