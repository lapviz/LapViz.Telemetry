using System.IO.Compression;
using System.Text.Json;
using LapViz.Telemetry.Domain;
using LapViz.Telemetry.IO;

namespace LapViz.Telemetry.Tests.IO;

/// <summary>
/// The example files of the specification (docs/examples): read by the library with the values they were written with.
/// Regenerate them with <c>LAPVIZ_WRITE_EXAMPLES=1 dotnet test --filter LapVizExamplesTests</c> after a change of the format.
/// </summary>
public class LapVizExamplesTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 12, 10, 0, 0, TimeSpan.FromHours(2));

    // A fictitious oval of 1 km, 10 Hz: out lap, two timed laps (61.240 s and 60.115 s), an incomplete in lap
    private static readonly double[] LapTimes = { 61.240, 60.115 };
    private const double OutLap = 18.0;
    private const double InLap = 12.5;
    private const double OriginLatitude = 50.5;
    private const double OriginLongitude = 5.5;

    private static string ExamplesFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "docs")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "docs", "examples");
    }

    /// <summary>The session of the examples, with samples or lap times only.</summary>
    internal static LapVizFile Example(bool withSamples)
    {
        var file = new LapVizFile(Start) { Generator = new LapVizGenerator { Name = "LapViz.Telemetry examples", Version = "2" } };
        file.Session.Driver = "Alex Example";
        file.Session.Vehicle = "Kart, 125 cc";
        file.Session.Device = new LapVizDevice { Brand = "LapViz", Model = "Example logger", Serial = "0001" };
        file.Session.Circuit = new LapVizCircuit { Code = "example-oval", Name = "Example oval (fictitious)", CountryCode = "BE" };
        file.Session.Description = "Example file of the LapViz format specification";
        file.Session.Setup = new VehicleSetup { Tyres = "Slick", TyreCondition = TyreCondition.Scrubbed, TyreLaps = 12, PressureFront = 0.7, PressureRear = 0.75, GearingFront = 11, GearingRear = 78, Feeling = 4 };
        file.Session.Notes = "Generated: the positions draw an oval, the speed and the RPM follow it.";
        file.Session.Properties["weather"] = "dry";

        var timing = new LapVizTimingSource { Source = "LapViz", IsDevice = true, Circuit = new LapVizCircuit { Code = "example-oval" } };
        timing.Laps.Add(new LapVizLap { Number = 0, Start = 0, Duration = OutLap });
        var start = OutLap;
        for (var index = 0; index < LapTimes.Length; index++)
        {
            var lap = new LapVizLap { Number = index + 1, Start = start, Duration = LapTimes[index] };
            var third = Math.Round(LapTimes[index] / 3, 3);
            lap.Sectors.Add(third);
            lap.Sectors.Add(third);
            lap.Sectors.Add(Math.Round(LapTimes[index] - (2 * third), 3));
            timing.Laps.Add(lap);
            start += LapTimes[index];
        }

        timing.Laps.Add(new LapVizLap { Number = LapTimes.Length + 1, Start = start }); // In lap: incomplete
        file.Timing.Add(timing);

        if (withSamples)
        {
            var duration = start + InLap;
            var count = (int)(duration * 10);
            var time = Enumerable.Range(0, count).Select(i => (long)i * 100_000).ToArray();
            double Angle(int i) => 2 * Math.PI * ((i / 10.0) % 60.5) / 60.5;
            file.SetTime(time);
            file.AddChannel(new LapVizChannel("Latitude", "deg"), Enumerable.Range(0, count).Select(i => OriginLatitude + (0.0012 * Math.Sin(Angle(i)))).ToArray());
            file.AddChannel(new LapVizChannel("Longitude", "deg"), Enumerable.Range(0, count).Select(i => OriginLongitude + (0.0030 * Math.Cos(Angle(i)))).ToArray());
            file.AddChannel(new LapVizChannel("GPS Speed", "km/h", LapVizValueType.Float32), Enumerable.Range(0, count).Select(i => Math.Round(60 + (25 * Math.Cos(2 * Angle(i))), 2)).ToArray());
            file.AddChannel(new LapVizChannel("RPM", "rpm", LapVizValueType.Float32), Enumerable.Range(0, count).Select(i => i % 97 == 0 ? double.NaN : Math.Round(11000 + (3000 * Math.Cos(2 * Angle(i))))).ToArray());
        }

        return file;
    }

    public static IEnumerable<object[]> Examples() => new[]
    {
        new object[] { "binary.lvz", LapVizSampleEncoding.Binary, true, true },
        new object[] { "csv.lvz", LapVizSampleEncoding.Csv, false, true },
        new object[] { "laptimes-only.lvz", LapVizSampleEncoding.Binary, true, false },
    };

    [Theory]
    [MemberData(nameof(Examples))]
    public void TheExampleFilesAreReadWithTheirValues(string name, LapVizSampleEncoding encoding, bool shuffle, bool withSamples)
    {
        var path = Path.Combine(ExamplesFolder(), name);
        var expected = Example(withSamples);
        if (Environment.GetEnvironmentVariable("LAPVIZ_WRITE_EXAMPLES") == "1")
        {
            Directory.CreateDirectory(ExamplesFolder());
            expected.Write(path, new LapVizWriteOptions { Encoding = encoding, ByteShuffle = shuffle });
            using var archive = ZipFile.OpenRead(path);
            using var manifest = archive.GetEntry(LapVizFile.ManifestEntry)!.Open();
            using var copy = File.Create(Path.ChangeExtension(path, ".lapviz.json"));
            manifest.CopyTo(copy); // Readable on GitHub without opening the archive
        }

        var read = LapVizFile.Read(path);

        Assert.Equal(Start, read.Session.Start);
        Assert.Equal("Alex Example", read.Session.Driver);
        Assert.Equal("example-oval", read.Session.Circuit!.Code);
        Assert.Equal(TyreCondition.Scrubbed, read.Session.Setup!.TyreCondition);
        var laps = read.PreferredTiming!.Laps;
        Assert.Equal(new[] { 0, 1, 2, 3 }, laps.Select(x => x.Number));
        Assert.Null(laps[3].Duration);
        Assert.Equal(60.115, laps.Where(x => x.Number > 0 && x.Duration != null).Min(x => x.Duration));
        Assert.Equal(expected.Count, read.Count);
        Assert.Equal(expected.Channels.Select(x => x.Name), read.Channels.Select(x => x.Name));
        if (withSamples)
        {
            Assert.True(read.TryGetValues("RPM", out var rpm));
            Assert.True(double.IsNaN(rpm[0]), "A missing value");
            Assert.True(read.TryGetValues("Latitude", out var latitude));
            Assert.True(expected.TryGetValues("Latitude", out var original));
            Assert.Equal(original, latitude);
        }

        // The manifest committed next to the archive is the one inside it
        using var zip = ZipFile.OpenRead(path);
        using var inside = new StreamReader(zip.GetEntry(LapVizFile.ManifestEntry)!.Open());
        Assert.Equal(JsonDocument.Parse(inside.ReadToEnd()).RootElement.ToString(),
            JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(path, ".lapviz.json"))).RootElement.ToString());
    }
}
