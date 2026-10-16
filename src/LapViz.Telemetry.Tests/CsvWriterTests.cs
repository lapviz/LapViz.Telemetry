using System.Globalization;
using LapViz.Telemetry.IO;
using LapViz.Telemetry.Tests.IO;
using LapViz.Telemetry.Tests.Services;

namespace LapViz.Telemetry.Tests;

public class CsvWriterTests
{
    private static string[] Lines(MemoryStream stream) =>
        new StreamReader(stream).ReadToEnd().Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.Length > 0).ToArray();

    [Fact]
    public void EverySampleIsARowWithItsLapAndExactValues()
    {
        var lines = Lines(new CsvWriter().WriteAll(LapVizFileTests.Sample().ToDeviceSessionData()));

        Assert.Equal(new[]
        {
            "Time (s),Lap,Lap time (s),Latitude,RPM,\"Weird, \"\"name\"\"\"",
            "0,0,,50.98213456789,10000.5,1E-300", // Before the out lap (0.05 s)
            "0.1,0,0.05,50.98213556789,10100,-0.1",
            "0.200001,0,0.150001,,10200.25,3.14159265358979", // A missing latitude: an empty cell
        }, lines);
    }

    [Fact]
    public void TheLapsOfTheSessionFollowEachOther()
    {
        var session = LapVizReadWriteTests.BuildSessionWithEventsFromCsv();

        var rows = Lines(new CsvWriter().WriteAll(session)).Skip(1).Select(x => x.Split(',')).ToList();

        Assert.Equal(session.TelemetryData.Count, rows.Count);
        Assert.All(rows, x => Assert.Equal(3 + session.TelemetryChannels.Count, x.Length));
        var laps = rows.Select(x => int.Parse(x[1], CultureInfo.InvariantCulture)).Where(x => x > 0).ToList();
        Assert.True(laps.Distinct().Count() > 5);
        Assert.True(laps.Zip(laps.Skip(1), (a, b) => b >= a).All(x => x), "The laps in their order");
        var times = rows.Select(x => double.Parse(x[0], CultureInfo.InvariantCulture)).ToList();
        Assert.True(times.Zip(times.Skip(1), (a, b) => b >= a).All(x => x), "The time from the start of the session, increasing");
        Assert.Equal(times.Count, times.Distinct().Count()); // Not rounded to the second
    }

    [Fact]
    public void AnExistingFileIsReplacedOnlyWhenAsked()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, new string('x', 100000));
            var data = LapVizFileTests.Sample().ToDeviceSessionData();

            new CsvWriter().WriteAll(data, path, false);
            Assert.StartsWith("xxx", File.ReadAllText(path));

            new CsvWriter().WriteAll(data, path, true);
            Assert.Equal(4, File.ReadAllLines(path).Length); // Truncated: nothing of the previous file
        }
        finally
        {
            File.Delete(path);
        }
    }
}
