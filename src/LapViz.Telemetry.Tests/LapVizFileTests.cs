using System.IO.Compression;
using System.Text;
using LapViz.Telemetry.Domain;
using LapViz.Telemetry.IO;

namespace LapViz.Telemetry.Tests.IO;

public class LapVizFileTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 9, 12, 10, 0, 0, 123, TimeSpan.FromHours(2));

    /// <summary>Three samples, a float64 position, a float32 channel, a missing value, two timing sources.</summary>
    private static LapVizFile Sample()
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
    [InlineData("""{ "format": "lapviz", "version": 3, "session": { "start": "2026-01-01T00:00:00Z" }, "channels": [] }""", "version 3")]
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

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var stream = zip.CreateEntry(name).Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}
