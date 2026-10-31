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
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.FromHours(2));

    // A fictitious oval of 1 km, 10 Hz: out lap, two timed laps (61.240 s and 60.115 s), an incomplete in lap
    private static readonly double[] LapTimes = { 61.240, 60.115 };
    private const double OutLap = 18.0;
    private const double InLap = 12.5;
    private const double OriginLatitude = 50.5;
    private const double OriginLongitude = 5.5;
    private const double Period = 60.5;

    private static double Angle(double seconds) => 2 * Math.PI * (seconds % Period) / Period;

    /// <summary>A timing line across the oval where the car is at <paramref name="seconds"/>, 30 m on each side.</summary>
    private static CircuitGeoLine LineAt(double seconds)
    {
        var angle = Angle(seconds);
        var latitude = OriginLatitude + (0.0012 * Math.Sin(angle));
        var longitude = OriginLongitude + (0.0030 * Math.Cos(angle));

        // Across the track: perpendicular to the direction (0.0012 cos, -0.0030 sin), in metres then in degrees
        var north = 0.0012 * Math.Cos(angle) * 111_320;
        var east = -0.0030 * Math.Sin(angle) * 111_320 * Math.Cos(OriginLatitude * Math.PI / 180);
        var length = Math.Sqrt((north * north) + (east * east));
        var dLatitude = 30 * -east / length / 111_320;
        var dLongitude = 30 * north / length / (111_320 * Math.Cos(OriginLatitude * Math.PI / 180));
        return new CircuitGeoLine(
            new GeoCoordinates(Math.Round(latitude - dLatitude, 7), Math.Round(longitude - dLongitude, 7)),
            new GeoCoordinates(Math.Round(latitude + dLatitude, 7), Math.Round(longitude + dLongitude, 7)));
    }

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
        file.Session.Clock = LapVizClock.Gps;

        // The lines the logger timed with: the start line where lap 1 starts, then the sector lines
        var third = Math.Round(LapTimes[0] / 3, 3);
        var circuit = new LapVizCircuit { Code = "example-oval", Type = CircuitType.Closed };
        circuit.Lines.Add(LineAt(OutLap));
        circuit.Lines.Add(LineAt(OutLap + third));
        circuit.Lines.Add(LineAt(OutLap + (2 * third)));

        var timing = new LapVizTimingSource { Source = "LapViz", IsDevice = true, Circuit = circuit };
        timing.Laps.Add(new LapVizLap { Number = 0, Start = 0, Duration = OutLap, Flags = { LapVizLapFlags.PitOut } });
        var start = OutLap;
        for (var index = 0; index < LapTimes.Length; index++)
        {
            var lap = new LapVizLap { Number = index + 1, Start = start, Duration = LapTimes[index] };
            var sector = Math.Round(LapTimes[index] / 3, 3);
            lap.Sectors.Add(sector);
            lap.Sectors.Add(sector);
            lap.Sectors.Add(Math.Round(LapTimes[index] - (2 * sector), 3));
            timing.Laps.Add(lap);
            start += LapTimes[index];
        }

        timing.Laps.Add(new LapVizLap { Number = LapTimes.Length + 1, Start = start, Flags = { LapVizLapFlags.PitIn } }); // In lap: incomplete
        file.Timing.Add(timing);

        // The best lap as typed in from the results sheet of the track
        file.Timing.Add(new LapVizTimingSource { Source = "Results sheet", IsDeclared = true, Laps = { new LapVizLap { Number = 2, Start = OutLap + LapTimes[0], Duration = 60.12 } } });
        file.Events.Add(new LapVizEvent { Time = 95.2, Type = "marker", Label = "Kerb too high in turn 2", Source = "driver" });

        if (withSamples)
        {
            var duration = start + InLap;
            var count = (int)(duration * 10);
            var time = Enumerable.Range(0, count).Select(i => (long)i * 100_000).ToArray();
            file.SetTime(time);
            file.AddChannel(new LapVizChannel("Latitude", "deg") { Rate = 10, Origin = "gps" }, Enumerable.Range(0, count).Select(i => OriginLatitude + (0.0012 * Math.Sin(Angle(i / 10.0)))).ToArray());
            file.AddChannel(new LapVizChannel("Longitude", "deg") { Rate = 10, Origin = "gps" }, Enumerable.Range(0, count).Select(i => OriginLongitude + (0.0030 * Math.Cos(Angle(i / 10.0)))).ToArray());
            file.AddChannel(new LapVizChannel("GPS Speed", "km/h", LapVizValueType.Float32) { Rate = 10, Origin = "gps" }, Enumerable.Range(0, count).Select(i => Math.Round(60 + (25 * Math.Cos(2 * Angle(i / 10.0))), 2)).ToArray());
            file.AddChannel(new LapVizChannel("RPM", "rpm", LapVizValueType.Float32) { Rate = 20, Origin = "ecu" }, Enumerable.Range(0, count).Select(i => i % 97 == 0 ? double.NaN : Math.Round(11000 + (3000 * Math.Cos(2 * Angle(i / 10.0))))).ToArray());
            file.AddChannel(new LapVizChannel("Gear", null, LapVizValueType.Float32) { Origin = "computed", Interpolation = LapVizInterpolation.Step }, Enumerable.Range(0, count).Select(i => Math.Cos(2 * Angle(i / 10.0)) > 0 ? 2.0 : 1.0).ToArray());
        }

        return file;
    }

    /// <summary>
    /// The session of the examples in version 3: the GPS of <c>binary.lvz</c> (10 Hz), the engine at 50 Hz, the water
    /// temperature at 1 Hz and the accelerometer at 100 Hz, stored as integers.
    /// </summary>
    internal static LapVizFile MultiRateExample()
    {
        var file = Example(withSamples: false);
        var gps = Example(withSamples: true);
        file.MainGroup.Name = "gps";
        file.SetTime(gps.Time);
        foreach (var name in new[] { "Latitude", "Longitude", "GPS Speed" })
        {
            var index = gps.Channels.ToList().FindIndex(x => x.Name == name);
            file.AddChannel(gps.Channels[index], gps.Values(index));
        }

        var duration = gps.Time[gps.Count - 1] / 1e6;
        LapVizSampleGroup Group(string name, int rate)
        {
            var group = file.AddGroup(name);
            group.SetTime(Enumerable.Range(0, (int)(duration * rate) + 1).Select(i => i * (1_000_000L / rate)).ToArray());
            return group;
        }

        double[] Values(LapVizSampleGroup group, Func<double, double> value) => group.Time.Select(t => value(t / 1e6)).ToArray();

        var engine = Group("engine", 50);
        engine.AddChannel(new LapVizChannel("RPM", "rpm", LapVizValueType.UInt16) { Rate = 50, Origin = "ecu" }, Values(engine, t => Math.Round(11000 + (3000 * Math.Cos(2 * Angle(t))))));
        engine.AddChannel(new LapVizChannel("Gear", null, LapVizValueType.UInt8) { Origin = "computed", Interpolation = LapVizInterpolation.Step }, Values(engine, t => Math.Cos(2 * Angle(t)) > 0 ? 2.0 : 1.0));

        var temperatures = Group("temperatures", 1);
        temperatures.AddChannel(new LapVizChannel("Water Temp", "°C", LapVizValueType.Int16) { Scale = 0.1, Rate = 1, Origin = "analog" }, Values(temperatures, t => Math.Round(45 + (10 * (1 - Math.Exp(-t / 60))), 1)));

        var imu = Group("imu", 100);
        imu.AddChannel(new LapVizChannel("AccelerometerX", "g", LapVizValueType.Int16) { Scale = 0.001, Rate = 100, Origin = "imu" }, Values(imu, t => Math.Round(0.4 * Math.Sin(2 * Angle(t)), 3)));
        imu.AddChannel(new LapVizChannel("AccelerometerY", "g", LapVizValueType.Int16) { Scale = 0.001, Rate = 100, Origin = "imu" }, Values(imu, t => Math.Round(1.1 * Math.Cos(2 * Angle(t)), 3)));
        return file;
    }

    [Fact]
    public void TheVersion3ExampleIsReadWithItsValues()
    {
        var path = Path.Combine(ExamplesFolder(), "multirate.lvz");
        var expected = MultiRateExample();
        if (Environment.GetEnvironmentVariable("LAPVIZ_WRITE_EXAMPLES") == "1")
        {
            expected.Write(path);
            using var archive = ZipFile.OpenRead(path);
            using var manifest = archive.GetEntry(LapVizFile.ManifestEntry)!.Open();
            using var copy = File.Create(Path.ChangeExtension(path, ".lapviz.json"));
            manifest.CopyTo(copy);
        }

        var read = LapVizFile.Read(path);

        Assert.Equal(new[] { "gps", "engine", "temperatures", "imu" }, read.Groups.Select(x => x.Name));
        Assert.Equal(new[] { 1518, 7585, 152, 15170 }, read.Groups.Select(x => x.Count));
        Assert.Equal(Example(withSamples: true).Values(0), read.Values(0)); // The positions of binary.lvz
        for (int g = 1; g < expected.Groups.Count; g++)
        {
            Assert.Equal(expected.Groups[g].Time, read.Groups[g].Time);
            for (int k = 0; k < expected.Groups[g].Channels.Count; k++)
            {
                var values = read.Groups[g].Values(k);
                Assert.All(expected.Groups[g].Values(k).Select((x, i) => Math.Abs(x - values[i])), x => Assert.True(x < 1e-9));
            }
        }

        using var zip = ZipFile.OpenRead(path);
        using var inside = new StreamReader(zip.GetEntry(LapVizFile.ManifestEntry)!.Open());
        Assert.Equal(JsonDocument.Parse(inside.ReadToEnd()).RootElement.ToString(),
            JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(path, ".lapviz.json"))).RootElement.ToString());
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
        Assert.Equal("gps", read.Session.Clock);
        Assert.Equal(3, read.PreferredTiming.Circuit!.Lines.Count);
        Assert.Equal(new[] { "pitOut" }, laps[0].Flags);
        Assert.True(read.Timing[1].IsDeclared);
        Assert.Equal("marker", Assert.Single(read.Events).Type);
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
