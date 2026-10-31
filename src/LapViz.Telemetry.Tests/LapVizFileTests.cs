using System.IO.Compression;
using System.Text;
using LapViz.Telemetry.Domain;
using LapViz.Telemetry.IO;

namespace LapViz.Telemetry.Tests.IO;

public class LapVizFileTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 9, 12, 10, 0, 0, 123, TimeSpan.FromHours(2));

    /// <summary>Three samples, a float64 position, a float32 channel, a missing value, two timing sources.</summary>
    internal static LapVizFile Sample()
    {
        var file = new LapVizFile(Start)
        {
            Generator = new LapVizGenerator { Name = "Tests", Version = "1.0" },
        };
        file.Session.Driver = "Alice Racer";
        file.Session.Vehicle = "OTK";
        file.Session.Device = new LapVizDevice { Brand = "AiM", Model = "Mychron 5", Serial = "51234" };
        file.Session.Circuit = new LapVizCircuit { Code = "genk", Name = "Karting Genk", CountryCode = "BE" };
        file.Session.SourceFile = "alice.xrk";
        file.Session.Properties["tyres"] = "MG Red";
        file.Session.Setup = new VehicleSetup
        {
            Tyres = "MG Red",
            TyreCondition = TyreCondition.Used,
            TyreLaps = 42,
            PressureFront = 0.65,
            PressureRear = 0.7,
            GearingFront = 11,
            GearingRear = 80,
            Chassis = "Medium axle",
            Feeling = 4,
        };
        file.Session.Notes = "Oversteer out of turn 3";

        file.SetTime(new long[] { 0, 100_000, 200_001 });
        file.AddChannel(new LapVizChannel("Latitude", "deg"), new[] { 50.98213456789, 50.98213556789, double.NaN });
        file.AddChannel(new LapVizChannel("RPM", "rpm", LapVizValueType.Float32), new[] { 10000.5, 10100, 10200.25 });
        file.AddChannel(new LapVizChannel("Weird, \"name\"", null, LapVizValueType.Float64), new[] { 1e-300, -0.1, 3.14159265358979 });

        file.Timing.Add(new LapVizTimingSource
        {
            Source = "AiM",
            IsDevice = true,
            Circuit = new LapVizCircuit { Code = "genk" },
            Laps =
            {
                new LapVizLap { Number = 0, Start = 0.05, Duration = 21.428 },
                new LapVizLap { Number = 1, Start = 21.478, Duration = 73.863, Sectors = { 24.511, 25.903, 23.449 } },
            },
        });
        file.Timing.Add(new LapVizTimingSource { Source = "LapViz", Laps = { new LapVizLap { Number = 1, Start = 21.5, Duration = 73.8 } } });
        return file;
    }

    [Theory]
    [InlineData(LapVizSampleEncoding.Binary, true, CompressionLevel.Optimal)]
    [InlineData(LapVizSampleEncoding.Binary, false, CompressionLevel.NoCompression)]
    [InlineData(LapVizSampleEncoding.Csv, false, CompressionLevel.Optimal)]
    public void RoundTripIsLossless(LapVizSampleEncoding encoding, bool shuffle, CompressionLevel compression)
    {
        var original = Sample();

        var bytes = original.ToBytes(new LapVizWriteOptions { Encoding = encoding, ByteShuffle = shuffle, Compression = compression });
        var read = LapVizFile.Read(new MemoryStream(bytes));

        Assert.Equal(original.Time, read.Time);
        Assert.Equal(Start, read.Session.Start);
        Assert.Equal(Start.Offset, read.Session.Start.Offset);
        Assert.Equal(new[] { "Latitude", "RPM", "Weird, \"name\"" }, read.Channels.Select(x => x.Name));
        Assert.Equal("rpm", read.Channels[1].Unit);
        Assert.Equal(LapVizValueType.Float32, read.Channels[1].Type);

        Assert.True(read.TryGetValues("latitude", out var latitude));
        Assert.Equal(50.98213456789, latitude[0]);
        Assert.True(double.IsNaN(latitude[2]));
        Assert.True(read.TryGetValues("RPM", out var rpm));
        Assert.Equal(new[] { 10000.5, 10100, 10200.25 }, rpm);
        Assert.True(read.TryGetValues("Weird, \"name\"", out var weird));
        Assert.Equal(new[] { 1e-300, -0.1, 3.14159265358979 }, weird);
        Assert.False(read.TryGetValues("Speed", out _));

        Assert.Equal("Alice Racer", read.Session.Driver);
        Assert.Equal("OTK", read.Session.Vehicle);
        Assert.Equal("51234", read.Session.Device!.Serial);
        Assert.Equal("BE", read.Session.Circuit!.CountryCode);
        Assert.Equal("alice.xrk", read.Session.SourceFile);
        Assert.Equal("MG Red", read.Session.Properties["tyres"]);
        Assert.Equal("Tests", read.Generator!.Name);

        var setup = read.Session.Setup!;
        Assert.Equal("MG Red", setup.Tyres);
        Assert.Equal(TyreCondition.Used, setup.TyreCondition);
        Assert.Equal(42, setup.TyreLaps);
        Assert.Equal(0.65, setup.PressureFront);
        Assert.Equal(0.7, setup.PressureRear);
        Assert.Equal(11, setup.GearingFront);
        Assert.Equal(80, setup.GearingRear);
        Assert.Null(setup.Carburation);
        Assert.Equal("Medium axle", setup.Chassis);
        Assert.Equal(4, setup.Feeling);
        Assert.Equal("Oversteer out of turn 3", read.Session.Notes);

        Assert.Equal(2, read.Timing.Count);
        Assert.True(read.Timing[0].IsDevice);
        Assert.False(read.Timing[1].IsDevice);
        Assert.Same(read.Timing[0], read.PreferredTiming);
        Assert.Equal(new[] { 24.511, 25.903, 23.449 }, read.Timing[0].Laps[1].Sectors);
        Assert.Equal(21.478, read.Timing[0].Laps[1].Start);
        Assert.Equal("genk", read.Timing[0].Circuit!.Code);
    }

    [Fact]
    public void ShuffledSensorDataCompressesBetter()
    {
        var file = new LapVizFile(Start);
        var count = 20_000;
        file.SetTime(Enumerable.Range(0, count).Select(i => (long)i * 40_000).ToArray());
        file.AddChannel(new LapVizChannel("Latitude", "deg"), Enumerable.Range(0, count).Select(i => 50.98 + (Math.Sin(i / 500.0) * 0.002)).ToArray());
        file.AddChannel(new LapVizChannel("GPS Speed", "km/h"), Enumerable.Range(0, count).Select(i => 80 + (40 * Math.Sin(i / 300.0))).ToArray());

        var shuffled = file.ToBytes(new LapVizWriteOptions { ByteShuffle = true }).Length;
        var plain = file.ToBytes(new LapVizWriteOptions { ByteShuffle = false }).Length;
        var raw = count * (8 + 8 + 8);

        Assert.True(shuffled < plain, $"shuffled {shuffled} vs {plain}");
        Assert.True(shuffled < raw * 0.75, $"shuffled {shuffled} vs raw {raw}");
    }

    [Fact]
    public void AFileWrittenFromTheSpecificationIsRead()
    {
        // Written by hand from docs/lapviz-format.md: CSV samples, members in any order, unknown members and entries
        const string manifest = """
            {
              "version": 2,
              "format": "lapviz",
              "future": { "ignored": true },
              "session": { "start": "2026-09-12T08:00:00Z", "driver": "Bob" },
              "channels": [ { "name": "GPS Speed", "unit": "km/h" }, { "name": "RPM", "type": "float32" } ],
              "samples": { "file": "samples.csv", "encoding": "csv" },
              "timing": [ { "source": "Manual", "laps": [ { "number": 1, "start": 0.5, "duration": 1.25, "sectors": [ 0.5, 0.75 ] } ] } ]
            }
            """;
        const string csv = "time,GPS Speed,RPM\r\n0,10.5,8000\r\n0.25,,8100\r\n1.000001,12,\r\n";

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "acme/notes.txt", "Extension entry");
            Write(zip, "samples.csv", csv);
            Write(zip, "lapviz.json", manifest);
        }

        stream.Position = 0;
        Assert.True(LapVizFile.IsLapVizArchive(stream));
        stream.Position = 0;
        var file = LapVizFile.Read(stream);

        Assert.Equal(new long[] { 0, 250_000, 1_000_001 }, file.Time);
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero), file.Session.Start);
        Assert.Equal(LapVizValueType.Float64, file.Channels[0].Type);
        Assert.True(file.TryGetValues("GPS Speed", out var speed));
        Assert.True(double.IsNaN(speed[1]));
        Assert.True(file.TryGetValues("RPM", out var rpm));
        Assert.True(double.IsNaN(rpm[2]));
        Assert.Equal(1.25, file.PreferredTiming!.Laps[0].Duration);
    }

    [Theory]
    [InlineData("""{ "format": "lapviz", "version": 4, "session": { "start": "2026-01-01T00:00:00Z" }, "groups": [] }""", "version 4")]
    [InlineData("""{ "format": "lapviz", "version": 3, "session": { "start": "2026-01-01T00:00:00Z" }, "channels": [] }""", "missing groups")]
    [InlineData("""{ "format": "lapviz", "version": 3, "session": { "start": "2026-01-01T00:00:00Z" }, "groups": [ { "name": "GPS", "channels": [] } ] }""", "invalid group name")]
    [InlineData("""{ "format": "lapviz", "version": 3, "session": { "start": "2026-01-01T00:00:00Z" }, "groups": [ { "name": "a", "channels": [] }, { "name": "a", "channels": [] } ] }""", "duplicate group")]
    [InlineData("""{ "format": "lapviz", "version": 3, "session": { "start": "2026-01-01T00:00:00Z" }, "groups": [ { "name": "a", "channels": [ { "name": "X" } ] }, { "name": "b", "channels": [ { "name": "x" } ] } ] }""", "duplicate channel")]
    [InlineData("""{ "format": "lapviz", "version": 3, "session": { "start": "2026-01-01T00:00:00Z" }, "groups": [ { "name": "a", "channels": [ { "name": "X", "type": "int16", "scale": 0 } ] } ] }""", "scale")]
    [InlineData("""{ "format": "lapviz", "version": 3, "session": { "start": "2026-01-01T00:00:00Z" }, "groups": [ { "name": "a", "channels": [ { "name": "X", "type": "int64" } ] } ] }""", "unknown type")]
    [InlineData("""{ "format": "other", "version": 2, "session": { "start": "2026-01-01T00:00:00Z" }, "channels": [] }""", "format")]
    [InlineData("""{ "format": "lapviz", "version": 2, "channels": [] }""", "session")]
    [InlineData("""{ "format": "lapviz", "version": 2, "session": { "start": "2026-01-01T00:00:00Z" }, "channels": [ { "name": "A" }, { "name": "a" } ] }""", "duplicate")]
    [InlineData("""{ "format": "lapviz", "version": 2, "session": { "start": "2026-01-01T00:00:00Z" }, "channels": [], "samples": { "file": "samples.bin", "encoding": "binary", "count": 1 } }""", "Missing samples")]
    [InlineData("""{ "format": "lapviz", "version": 2, "session": { "start": "2026-01-01T00:00:00Z" }, "channels": [ { "name": "A", "type": "int8" } ] }""", "unknown type")]
    [InlineData("not json", "JSON")]
    public void InvalidFilesAreRejected(string manifest, string message)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "lapviz.json", manifest);
        }

        stream.Position = 0;
        var error = Assert.Throws<InvalidDataException>(() => LapVizFile.Read(stream));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BinarySamplesOfTheWrongLengthAreRejected()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "lapviz.json", """{ "format": "lapviz", "version": 2, "session": { "start": "2026-01-01T00:00:00Z" }, "channels": [ { "name": "A", "type": "float32" } ], "samples": { "file": "samples.bin", "encoding": "binary", "count": 2 } }""");
            var entry = zip.CreateEntry("samples.bin");
            using var samples = entry.Open();
            samples.Write(new byte[(2 * 8) + 4]);
        }

        stream.Position = 0;
        var error = Assert.Throws<InvalidDataException>(() => LapVizFile.Read(stream));
        Assert.Contains("expected", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TimeMustIncrease()
    {
        var file = new LapVizFile(Start);

        Assert.Throws<ArgumentException>(() => file.SetTime(new long[] { 0, 10, 10 }));
        file.SetTime(new long[] { 0, 10 });
        Assert.Throws<ArgumentException>(() => file.AddChannel(new LapVizChannel("A"), new[] { 1.0 }));
        file.AddChannel(new LapVizChannel("A"), new[] { 1.0, 2.0 });
        Assert.Throws<ArgumentException>(() => file.AddChannel(new LapVizChannel("a"), new[] { 1.0, 2.0 }));
    }

    [Fact]
    public void OtherZipsAndTextAreNotVersion2()
    {
        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "data.lz", "#Format=LapViz Delimited Data");
        }

        zipStream.Position = 0;
        Assert.False(LapVizFile.IsLapVizArchive(zipStream));
        Assert.False(LapVizFile.IsLapVizArchive(new MemoryStream(Encoding.UTF8.GetBytes("#Format=LapViz Delimited Data"))));
    }

    [Fact]
    public void DeviceSessionDataRoundTrip()
    {
        var data = Sample().ToDeviceSessionData();

        Assert.Equal("Alice Racer", data.DriverDisplayName);
        Assert.Equal("AiM", data.DeviceBrand);
        Assert.Equal("Mychron 5", data.DeviceName);
        Assert.Equal("51234", data.DeviceId);
        Assert.Equal("genk", data.CircuitCode);
        Assert.Equal("AiM", data.Generator);
        Assert.True(data.IsTelemetryDevice);
        Assert.Equal(3, data.TelemetryData.Count);
        Assert.Equal(Start.AddTicks(2_000_010), data.TelemetryData[2].Timestamp);
        var first = (GeoTelemetryData)data.TelemetryData[0];
        Assert.Equal(50.98213456789, first.Latitude);
        Assert.Null(data.TelemetryData[2].Data[0]);

        var laps = data.Events.Where(x => x.Type == SessionEventType.Lap).ToList();
        var sectors = data.Events.Where(x => x.Type == SessionEventType.Sector).ToList();
        Assert.Equal(2, laps.Count);
        Assert.Equal(3, sectors.Count);
        Assert.Equal(TimeSpan.FromSeconds(73.863), laps[1].Time);
        Assert.Equal(laps[1].Timestamp, sectors[2].Timestamp);
        Assert.Single(data.TimingData);
        Assert.Equal("LapViz", data.TimingData[0].Generator);

        var back = LapVizFile.FromDeviceSessionData(data);

        Assert.Equal(new long[] { 0, 100_000, 200_001 }, back.Time);
        Assert.Equal(Start, back.Session.Start);
        Assert.Equal(2, back.Timing.Count);
        Assert.Equal(new[] { 24.511, 25.903, 23.449 }, back.Timing[0].Laps[1].Sectors.Select(x => Math.Round(x, 6)));
        Assert.Equal(21.478, back.Timing[0].Laps[1].Start, 6);
        Assert.Equal(LapVizValueType.Float32, back.Channels.Single(x => x.Name == "RPM").Type);
        Assert.Equal(LapVizValueType.Float64, back.Channels.Single(x => x.Name == "Latitude").Type);
        Assert.Equal("51234", back.Session.Device!.Serial);
        Assert.Equal(42, data.Setup!.TyreLaps);
        Assert.Equal(TyreCondition.Used, back.Session.Setup!.TyreCondition);
        Assert.Equal("Oversteer out of turn 3", back.Session.Notes);
    }

    [Fact]
    public void TheSetupIsWrittenAsInTheSpecification()
    {
        var file = new LapVizFile(Start);
        file.Session.Setup = new VehicleSetup { TyreCondition = TyreCondition.Scrubbed, PressureFront = 0.65 };

        using var zip = new ZipArchive(new MemoryStream(file.ToBytes()));
        using var reader = new StreamReader(zip.GetEntry("lapviz.json")!.Open());
        var manifest = reader.ReadToEnd();

        Assert.Contains("\"setup\":{\"tyreCondition\":\"scrubbed\",\"pressureFront\":0.65}", manifest.Replace(" ", string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty));
        Assert.DoesNotContain("notes", manifest);

        file.Session.Setup = new VehicleSetup();
        Assert.Null(LapVizFile.Read(new MemoryStream(file.ToBytes())).Session.Setup); // An empty sheet is not written
    }

    [Fact]
    public void InvalidSetupMembersAreIgnored()
    {
        const string manifest = """
            {
              "format": "lapviz", "version": 2, "channels": [],
              "session": {
                "start": "2026-09-12T08:00:00Z",
                "setup": { "tyres": "Vega", "tyreCondition": "melted", "tyreLaps": -3, "pressureFront": "0.65", "gearingRear": 80.5, "feeling": 9, "future": 1 }
              }
            }
            """;
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            Write(zip, "lapviz.json", manifest);

        stream.Position = 0;
        var setup = LapVizFile.Read(stream).Session.Setup!;

        Assert.Equal("Vega", setup.Tyres);
        Assert.Null(setup.TyreCondition);
        Assert.Null(setup.TyreLaps);
        Assert.Null(setup.PressureFront);
        Assert.Null(setup.GearingRear);
        Assert.Null(setup.Feeling);
    }

    [Fact]
    public void AMissedSectorKeepsThePlaceOfTheNextOnes()
    {
        // Sector 2 of lap 1 not crossed: sector 3 stays the third one (it used to become the second)
        var events = new List<SessionDataEvent>
        {
            new SessionDataEvent { Type = SessionEventType.Sector, LapNumber = 1, Sector = 1, Time = TimeSpan.FromSeconds(20), Timestamp = Start.AddSeconds(20) },
            new SessionDataEvent { Type = SessionEventType.Sector, LapNumber = 1, Sector = 3, Time = TimeSpan.FromSeconds(40), Timestamp = Start.AddSeconds(60) },
            new SessionDataEvent { Type = SessionEventType.Lap, LapNumber = 1, Time = TimeSpan.FromSeconds(60), Timestamp = Start.AddSeconds(60) },
        };

        var source = LapVizFile.TimingSource("LapViz", false, null, events, Start);

        Assert.Equal(new[] { 20.0, 0, 40 }, source.Laps[0].Sectors);
        var file = new LapVizFile(Start);
        file.Timing.Add(source);
        var back = LapVizFile.Read(new MemoryStream(file.ToBytes())).ToDeviceSessionData();
        Assert.Equal(new[] { 1, 3 }, back.Events.Where(x => x.Type == SessionEventType.Sector).Select(x => x.Sector).ToArray());
    }

    [Fact]
    public void IncompleteLapKeepsItsSectors()
    {
        var file = new LapVizFile(Start);
        file.Timing.Add(new LapVizTimingSource
        {
            Source = "AiM",
            IsDevice = true,
            Laps =
            {
                new LapVizLap { Number = 1, Start = 0, Duration = 60, Sectors = { 20, 20, 20 } },
                new LapVizLap { Number = 2, Start = 60, Sectors = { 21, 19.5 } },
            },
        });

        var read = LapVizFile.Read(new MemoryStream(file.ToBytes()));
        Assert.False(read.Timing[0].Laps[1].IsComplete);
        Assert.Equal(new[] { 21, 19.5 }, read.Timing[0].Laps[1].Sectors);

        var data = read.ToDeviceSessionData();
        Assert.Single(data.Events, x => x.Type == SessionEventType.Lap);
        Assert.Equal(5, data.Events.Count(x => x.Type == SessionEventType.Sector));

        var back = LapVizFile.FromDeviceSessionData(data);
        Assert.Equal(2, back.Timing[0].Laps.Count);
        Assert.Null(back.Timing[0].Laps[1].Duration);
        Assert.Equal(60, back.Timing[0].Laps[1].Start - back.Timing[0].Laps[0].Start, 6);
    }

    [Fact]
    public void ReaderAndWriterUseVersion2()
    {
        var data = Sample().ToDeviceSessionData();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lvz");
        try
        {
            new LapVizDataWriter().WriteAll(data, path, overwrite: true);

            Assert.True(LapVizFile.IsLapVizArchive(path));
            var reader = new LapVizDataReader();
            Assert.True(reader.IsDataCompatible(path));
            reader.Load(path);
            Assert.Equal(new[] { "Latitude", "RPM", "Weird, \"name\"" }, reader.GetTelemetryChannels());
            var read = reader.GetSessionData().Single();
            Assert.Equal(3, read.TelemetryData.Count);
            Assert.Equal(Path.GetFileName(path), read.OriginalFilename);
            Assert.Equal(32, read.SourceFileHash.Length);
            Assert.Equal(5, read.Events.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FixesWithoutChannelsKeepTheirPositionSpeedAltitudeAndAccuracy()
    {
        // A lap timer session: GPS fixes only, no telemetry channels
        var data = new DeviceSessionData("phone", string.Empty);
        data.TelemetryData.Add(new GeoTelemetryData(50.9821, 5.5012) { Timestamp = Start, Speed = 81.5, Accuracy = 3.2 });
        data.TelemetryData.Add(new GeoTelemetryData(50.9822, 5.5014) { Timestamp = Start.AddMilliseconds(100), Speed = 82.25, Accuracy = 3.1 });
        data.TelemetryData.Add(new GeoTelemetryData(0, 0) { Timestamp = Start.AddMilliseconds(200) });
        data.AddEvent(new SessionDataEvent { Type = SessionEventType.Lap, LapNumber = 1, Time = TimeSpan.FromMilliseconds(200), Timestamp = Start.AddMilliseconds(200) });

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lvz");
        try
        {
            new LapVizDataWriter().WriteAll(data, path, overwrite: true);

            var file = LapVizFile.Read(path);
            Assert.Equal(new[] { "Latitude", "Longitude", "GPS Speed", "GPS Accuracy" }, file.Channels.Select(x => x.Name));
            Assert.Equal("km/h", file.Channels.Single(x => x.Name == "GPS Speed").Unit);

            var reader = new LapVizDataReader();
            reader.Load(path);
            var fixes = reader.GetSessionData().Single().TelemetryData.Cast<GeoTelemetryData>().ToList();
            Assert.Equal(3, fixes.Count);
            Assert.Equal(50.9822, fixes[1].Latitude);
            Assert.Equal(5.5014, fixes[1].Longitude);
            Assert.Equal(82.25, fixes[1].Speed);
            Assert.Equal(3.1, fixes[1].Accuracy!.Value, 6);
            Assert.Null(fixes[2].Speed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheAdditionsToVersion2RoundTrip()
    {
        var file = Sample();
        file.Session.Clock = LapVizClock.Gps;
        file.Session.Circuit!.Type = CircuitType.Closed;
        file.Session.Circuit.Lines.Add(new CircuitGeoLine(new GeoCoordinates(50.98201, 5.33012), new GeoCoordinates(50.98215, 5.33031)));
        file.Session.Circuit.Lines.Add(new CircuitGeoLine(new GeoCoordinates(50.98302, 5.33112), new GeoCoordinates(50.98315, 5.33131)));
        file.Channels[1].Rate = 50;
        file.Channels[1].Origin = "ecu";
        file.Channels[2].Interpolation = LapVizInterpolation.Step;
        file.Timing[0].Laps[0].Flags.Add(LapVizLapFlags.PitOut);
        file.Timing[0].Laps[1].Flags.Add(LapVizLapFlags.Neutralized);
        file.Timing[0].Laps[1].Flags.Add("future-flag");
        file.Timing[0].Laps[1].Driver = "Bob Racer";
        file.Timing[1].IsDeclared = true;
        file.Events.Add(new LapVizEvent { Time = 40.5, Type = "flag", Label = "yellow", Source = "Transponder" });
        file.Events.Add(new LapVizEvent { Time = 12.25, Type = "marker", Label = "Vibration", Source = "driver" });

        foreach (var encoding in new[] { LapVizSampleEncoding.Binary, LapVizSampleEncoding.Csv })
        {
            var read = LapVizFile.Read(new MemoryStream(file.ToBytes(new LapVizWriteOptions { Encoding = encoding })));

            Assert.Equal("gps", read.Session.Clock);
            Assert.Equal(CircuitType.Closed, read.Session.Circuit!.Type);
            Assert.Equal(2, read.Session.Circuit.Lines.Count);
            Assert.Equal(50.98302, read.Session.Circuit.Lines[1].Start.Latitude);
            Assert.Equal(5.33131, read.Session.Circuit.Lines[1].End.Longitude);
            Assert.Null(read.Timing[0].Circuit!.Type);
            Assert.Equal(50, read.Channels[1].Rate);
            Assert.Equal("ecu", read.Channels[1].Origin);
            Assert.Equal(LapVizInterpolation.Linear, read.Channels[1].Interpolation);
            Assert.Equal(LapVizInterpolation.Step, read.Channels[2].Interpolation);
            Assert.Equal(new[] { "pitOut" }, read.Timing[0].Laps[0].Flags);
            Assert.Equal(new[] { "neutralized", "future-flag" }, read.Timing[0].Laps[1].Flags);
            Assert.Equal("Bob Racer", read.Timing[0].Laps[1].Driver);
            Assert.Null(read.Timing[0].Laps[0].Driver);
            Assert.False(read.Timing[0].IsDeclared);
            Assert.True(read.Timing[1].IsDeclared);
            Assert.Equal(new[] { "marker", "flag" }, read.Events.Select(x => x.Type)); // Ordered by time
            Assert.Equal("yellow", read.Events[1].Label);
            Assert.Equal("Transponder", read.Events[1].Source);
        }
    }

    [Fact]
    public void AFileWithoutTheAdditionsHasTheirDefaults()
    {
        var read = LapVizFile.Read(new MemoryStream(Sample().ToBytes()));

        Assert.Null(read.Session.Clock);
        Assert.Null(read.Session.Circuit!.Type);
        Assert.Empty(read.Session.Circuit.Lines);
        Assert.All(read.Channels, x => Assert.Null(x.Rate));
        Assert.All(read.Channels, x => Assert.Equal(LapVizInterpolation.Linear, x.Interpolation));
        Assert.All(read.Timing.SelectMany(x => x.Laps), x => Assert.Empty(x.Flags));
        Assert.Empty(read.Events);

        // Nothing is written for them
        using var zip = new ZipArchive(new MemoryStream(Sample().ToBytes()));
        using var reader = new StreamReader(zip.GetEntry("lapviz.json")!.Open());
        var manifest = reader.ReadToEnd();
        foreach (var member in new[] { "clock", "lines", "rate", "origin", "interpolation", "flags", "declared", "events" })
            Assert.DoesNotContain("\"" + member + "\"", manifest);
    }

    [Theory]
    [InlineData(LapVizSampleEncoding.Binary)]
    [InlineData(LapVizSampleEncoding.Csv)]
    public void DamagedSamplesAreRejected(LapVizSampleEncoding encoding)
    {
        var bytes = Sample().ToBytes(new LapVizWriteOptions { Encoding = encoding, Compression = CompressionLevel.NoCompression });
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
        using (var reader = new StreamReader(zip.GetEntry("lapviz.json")!.Open()))
            Assert.Matches("\"sha256\":\\s*\"[0-9a-f]{64}\"", reader.ReadToEnd());

        // One bit of the stored samples flipped: same length, the ZIP itself does not check its CRC here
        var damaged = Damage(bytes, encoding == LapVizSampleEncoding.Binary ? "samples.bin" : "samples.csv");

        var error = Assert.Throws<InvalidDataException>(() => LapVizFile.Read(new MemoryStream(damaged)));
        Assert.Contains("damaged", error.Message, StringComparison.Ordinal);
        Assert.NotNull(LapVizFile.Read(new MemoryStream(Sample().ToBytes(new LapVizWriteOptions { Encoding = encoding, Checksum = false }))));
    }

    [Fact]
    public void InvalidLapsAreNotBestLapsAndDeclaredLapsComeAfterMeasuredOnes()
    {
        var file = new LapVizFile(Start);
        file.Timing.Add(new LapVizTimingSource { Source = "Results sheet", IsDeclared = true, Laps = { new LapVizLap { Number = 1, Start = 0, Duration = 60 } } });
        file.Timing.Add(new LapVizTimingSource { Source = "Phone", Laps = { new LapVizLap { Number = 1, Start = 0, Duration = 60.2 } } });
        var invalid = new LapVizLap { Number = 2, Start = 60, Duration = 59, Flags = { LapVizLapFlags.Invalid } };

        Assert.Equal("Phone", file.PreferredTiming!.Source);
        Assert.False(invalid.IsValid);
        Assert.True(file.Timing[1].Laps[0].IsValid);
        Assert.False(new LapVizLap { Number = 3, Start = 119 }.IsValid);

        file.Timing.RemoveAt(1);
        Assert.Equal("Results sheet", file.PreferredTiming!.Source); // Declared laps are better than none
    }

    [Fact]
    public void LinesThatAreNotTwoValidPointsAreIgnored()
    {
        const string manifest = """
            {
              "format": "lapviz", "version": 2, "channels": [],
              "session": {
                "start": "2026-01-01T00:00:00Z",
                "circuit": {
                  "type": "spiral",
                  "lines": [
                    [ { "lat": 50.1, "lon": 5.1 }, { "lat": 50.2, "lon": 5.2 } ],
                    [ { "lat": 95, "lon": 5.1 }, { "lat": 50.2, "lon": 5.2 } ],
                    [ { "lat": 50.1, "lon": 5.1 } ],
                    [ [ 50.1, 5.1 ], [ 50.2, 5.2 ] ],
                    "start"
                  ]
                }
              },
              "events": [ { "time": 1, "type": "marker" }, { "type": "marker" }, { "time": 2 } ]
            }
            """;
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "lapviz.json", manifest);
        }

        stream.Position = 0;
        var file = LapVizFile.Read(stream);

        Assert.Null(file.Session.Circuit!.Type);
        Assert.Equal(50.2, Assert.Single(file.Session.Circuit.Lines).End.Latitude);
        Assert.Equal(1, Assert.Single(file.Events).Time);
    }

    [Fact]
    public void TheLinesOfTheCircuitAreExported()
    {
        var data = Sample().ToDeviceSessionData();
        data.CircuitConfiguration = new CircuitConfiguration
        {
            Code = "genk",
            Name = "Karting Genk",
            Type = CircuitType.Open,
            Segments =
            {
                new CircuitSegment { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(50.2, 5.2), new GeoCoordinates(50.21, 5.21)) },
                new CircuitSegment { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.1, 5.1), new GeoCoordinates(50.11, 5.11)) },
            },
        };

        var circuit = LapVizFile.Read(new MemoryStream(LapVizFile.FromDeviceSessionData(data).ToBytes())).Session.Circuit!;

        Assert.Equal(CircuitType.Open, circuit.Type);
        Assert.Equal(new[] { 50.1, 50.2 }, circuit.Lines.Select(x => x.Start.Latitude)); // In the order of the segments
    }

    /// <summary>The archive with one bit of the stored entry <paramref name="name"/> flipped.</summary>
    private static byte[] Damage(byte[] archive, string name)
    {
        var damaged = (byte[])archive.Clone();
        var header = Encoding.ASCII.GetBytes(name);
        for (int i = 0; i < damaged.Length - header.Length; i++)
        {
            // Local file header: signature PK\x03\x04, name at offset 30, data after the name and the extra field
            if (damaged[i] == 0x50 && damaged[i + 1] == 0x4b && damaged[i + 2] == 3 && damaged[i + 3] == 4 &&
                damaged.AsSpan(i + 30, header.Length).SequenceEqual(header))
            {
                var extra = damaged[i + 28] | (damaged[i + 29] << 8);
                damaged[i + 30 + header.Length + extra + 2] ^= 1;
                return damaged;
            }
        }

        throw new InvalidOperationException("No entry " + name);
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var stream = zip.CreateEntry(name).Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}
