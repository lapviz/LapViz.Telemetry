using System.IO.Compression;
using System.Text.Json;
using LapViz.Telemetry.Domain;
using LapViz.Telemetry.IO;

namespace LapViz.Telemetry.Tests.IO;

/// <summary>Version 3 of the LapViz format: groups of samples with their own time, integer channels.</summary>
public class LapVizVersion3Tests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.FromHours(2));

    /// <summary>GPS at 10 Hz (the first group), engine at 50 Hz with integer channels, a missing value in each.</summary>
    private static LapVizFile MultiRate()
    {
        var file = new LapVizFile(Start);
        file.MainGroup.Name = "gps";
        file.SetTime(Enumerable.Range(0, 11).Select(i => i * 100_000L).ToArray());
        file.AddChannel(new LapVizChannel("Latitude", "deg"), Enumerable.Range(0, 11).Select(i => 50.5 + (i * 0.0001)).ToArray());
        file.AddChannel(new LapVizChannel("GPS Speed", "km/h", LapVizValueType.Float32), Enumerable.Range(0, 11).Select(i => i == 4 ? double.NaN : 60.0 + i).ToArray());

        var engine = file.AddGroup("engine");
        engine.SetTime(Enumerable.Range(0, 51).Select(i => i * 20_000L).ToArray());
        engine.AddChannel(new LapVizChannel("RPM", "rpm", LapVizValueType.UInt16) { Rate = 50 }, Enumerable.Range(0, 51).Select(i => i == 7 ? double.NaN : 10_000.0 + (i * 20)).ToArray());
        engine.AddChannel(new LapVizChannel("Water Temp", "°C", LapVizValueType.Int16) { Scale = 0.1, Offset = 50 }, Enumerable.Range(0, 51).Select(i => 50 + (i * 0.5)).ToArray());
        engine.AddChannel(new LapVizChannel("Gear", null, LapVizValueType.UInt8) { Interpolation = LapVizInterpolation.Step }, Enumerable.Range(0, 51).Select(i => i < 25 ? 2.0 : 3.0).ToArray());
        engine.AddChannel(new LapVizChannel("Lateral", "g", LapVizValueType.Int8) { Scale = 0.05 }, Enumerable.Range(0, 51).Select(i => Math.Round((i - 25) * 0.05, 2)).ToArray());
        return file;
    }

    [Theory]
    [InlineData(LapVizSampleEncoding.Binary, true)]
    [InlineData(LapVizSampleEncoding.Binary, false)]
    [InlineData(LapVizSampleEncoding.Csv, false)]
    public void GroupsAndIntegerChannelsRoundTrip(LapVizSampleEncoding encoding, bool shuffle)
    {
        var file = MultiRate();
        Assert.Equal(3, file.WrittenVersion);

        var bytes = file.ToBytes(new LapVizWriteOptions { Encoding = encoding, ByteShuffle = shuffle });
        var read = LapVizFile.Read(new MemoryStream(bytes));

        Assert.Equal(new[] { "gps", "engine" }, read.Groups.Select(x => x.Name));
        Assert.Equal(file.Time, read.Time);
        Assert.Equal(file.Groups[1].Time, read.Groups[1].Time);
        Assert.Equal(new[] { "Latitude", "GPS Speed", "RPM", "Water Temp", "Gear", "Lateral" }, read.AllChannels.Select(x => x.Name));
        Assert.Same(read.Groups[1], read.FindChannel("rpm"));
        Assert.False(read.TryGetValues("RPM", out _)); // Not a channel of the first group

        var rpm = read.Groups[1].Channels[0];
        Assert.Equal(LapVizValueType.UInt16, rpm.Type);
        Assert.Equal(50, rpm.Rate);
        var temperature = read.Groups[1].Channels[1];
        Assert.Equal(LapVizValueType.Int16, temperature.Type);
        Assert.Equal(0.1, temperature.Scale);
        Assert.Equal(50, temperature.Offset);
        Assert.Equal(LapVizInterpolation.Step, read.Groups[1].Channels[2].Interpolation);

        for (int k = 0; k < 4; k++)
        {
            var expected = file.Groups[1].Values(k);
            var actual = read.Groups[1].Values(k);
            for (int i = 0; i < expected.Length; i++)
            {
                if (double.IsNaN(expected[i]))
                    Assert.True(double.IsNaN(actual[i]), "missing value " + i);
                else
                    Assert.Equal(expected[i], actual[i], 9);
            }
        }

        Assert.True(double.IsNaN(read.Values(1)[4]));

        using var zip = new ZipArchive(new MemoryStream(bytes));
        var extension = encoding == LapVizSampleEncoding.Binary ? ".bin" : ".csv";
        Assert.NotNull(zip.GetEntry("samples/gps" + extension));
        Assert.NotNull(zip.GetEntry("samples/engine" + extension));
        using var manifest = JsonDocument.Parse(zip.GetEntry("lapviz.json")!.Open());
        Assert.Equal(3, manifest.RootElement.GetProperty("version").GetInt32());
        Assert.False(manifest.RootElement.TryGetProperty("channels", out _));
        Assert.False(manifest.RootElement.TryGetProperty("samples", out _));
    }

    [Fact]
    public void IntegersAreSmallerThanFloats()
    {
        var file = MultiRate();

        // Time, then RPM and Water Temp on 2 bytes, Gear and Lateral on 1 byte
        using var zip = new ZipArchive(new MemoryStream(file.ToBytes(new LapVizWriteOptions { Compression = CompressionLevel.NoCompression })));
        Assert.Equal(51 * 14, zip.GetEntry("samples/engine.bin")!.Length);
    }

    [Fact]
    public void AFileThatFitsVersion2IsWrittenInVersion2()
    {
        var file = new LapVizFile(Start);
        file.SetTime(new long[] { 0, 100_000 });
        file.AddChannel(new LapVizChannel("RPM", "rpm", LapVizValueType.Float32), new[] { 9000.0, 9100 });
        file.AddGroup("unused"); // Empty: not written
        Assert.Equal(2, file.WrittenVersion);

        using (var zip = new ZipArchive(new MemoryStream(file.ToBytes())))
        {
            Assert.NotNull(zip.GetEntry("samples.bin"));
            using var manifest = JsonDocument.Parse(zip.GetEntry("lapviz.json")!.Open());
            Assert.Equal(2, manifest.RootElement.GetProperty("version").GetInt32());
            Assert.False(manifest.RootElement.TryGetProperty("groups", out _));
        }

        var read = LapVizFile.Read(new MemoryStream(file.ToBytes()));
        Assert.Equal(LapVizFile.DefaultGroupName, Assert.Single(read.Groups).Name);

        // One integer channel needs version 3
        var integer = new LapVizFile(Start);
        integer.SetTime(new long[] { 0 });
        integer.AddChannel(new LapVizChannel("Gear", null, LapVizValueType.UInt8), new[] { 1.0 });
        Assert.Equal(3, integer.WrittenVersion);
        Assert.Equal("main", Assert.Single(LapVizFile.Read(new MemoryStream(integer.ToBytes())).Groups).Name);
    }

    [Theory]
    [InlineData(LapVizValueType.UInt8, 1, 0, 255)] // The missing value
    [InlineData(LapVizValueType.UInt8, 1, 0, -1)]
    [InlineData(LapVizValueType.Int8, 1, 0, -128)]
    [InlineData(LapVizValueType.Int16, 0.1, 0, 3276.8)]
    [InlineData(LapVizValueType.UInt16, 1, 1000, 999)]
    public void AValueThatDoesNotFitItsTypeIsNotWritten(LapVizValueType type, double scale, double offset, double value)
    {
        var file = new LapVizFile(Start);
        file.SetTime(new long[] { 0 });
        file.AddChannel(new LapVizChannel("X", null, type) { Scale = scale, Offset = offset }, new[] { value });

        var error = Assert.Throws<InvalidOperationException>(() => file.ToBytes());
        Assert.Contains("does not fit", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LapVizValueType.Int8, -127, 127)]
    [InlineData(LapVizValueType.UInt8, 0, 254)]
    [InlineData(LapVizValueType.Int16, -32767, 32767)]
    [InlineData(LapVizValueType.UInt16, 0, 65534)]
    [InlineData(LapVizValueType.Int32, -2147483647, 2147483647)]
    public void TheWholeRangeOfAnIntegerTypeIsKept(LapVizValueType type, double min, double max)
    {
        var file = new LapVizFile(Start);
        file.SetTime(new long[] { 0, 1, 2 });
        file.AddChannel(new LapVizChannel("X", null, type), new[] { min, double.NaN, max });

        var values = LapVizFile.Read(new MemoryStream(file.ToBytes())).Values(0);

        Assert.Equal(min, values[0]);
        Assert.True(double.IsNaN(values[1]));
        Assert.Equal(max, values[2]);
    }

    [Fact]
    public void TheOtherFormatsGetTheTimeOfThePositions()
    {
        var file = MultiRate();
        var data = file.ToDeviceSessionData();

        Assert.Equal(11, data.TelemetryData.Count); // The 10 Hz of the GPS
        Assert.Equal(new[] { "Latitude", "GPS Speed", "RPM", "Water Temp", "Gear", "Lateral" }, data.TelemetryChannels);
        var sample = data.TelemetryData[5]; // 0.5 s: engine sample 25
        Assert.Equal(10_500, sample.Data[2]);
        Assert.Equal(62.5, sample.Data[3].GetValueOrDefault(), 9);
        Assert.Equal(3, sample.Data[4]);
        Assert.Equal(50.5005, ((GeoTelemetryData)sample).Latitude, 9);

        // The positions in another group than the first: still the time base
        var engineFirst = new LapVizFile(Start);
        engineFirst.MainGroup.Name = "engine";
        engineFirst.SetTime(new long[] { 0, 50_000, 100_000, 150_000 });
        engineFirst.AddChannel(new LapVizChannel("RPM"), new[] { 1000.0, 2000, 3000, 4000 });
        var gps = engineFirst.AddGroup("gps");
        gps.SetTime(new long[] { 25_000, 125_000 });
        gps.AddChannel(new LapVizChannel("Latitude", "deg"), new[] { 50.1, 50.2 });

        var converted = engineFirst.ToDeviceSessionData();
        Assert.Equal(2, converted.TelemetryData.Count);
        Assert.Equal(Start.AddTicks(250_000), converted.TelemetryData[0].Timestamp);
        Assert.Equal(1500, converted.TelemetryData[0].Data[0]);
        Assert.Equal(3500, converted.TelemetryData[1].Data[0]);
    }

    [Fact]
    public void WithOneTimeBaseBringsEveryChannelToTheTimeOfThePositions()
    {
        var file = MultiRate();
        file.Events.Add(new LapVizEvent { Time = 0.3, Type = "marker" });

        var flat = file.WithOneTimeBase();

        Assert.Single(flat.Groups);
        Assert.Equal("gps", flat.MainGroup.Name);
        Assert.Equal(file.Time, flat.Time);
        Assert.Equal(new[] { "Latitude", "GPS Speed", "RPM", "Water Temp", "Gear", "Lateral" }, flat.Channels.Select(x => x.Name));
        Assert.True(flat.TryGetValues("RPM", out var rpm));
        Assert.Equal(10_000, rpm[0]);
        Assert.Equal(10_500, rpm[5]); // 0.5 s: engine sample 25
        Assert.All(flat.Channels, x => Assert.False(x.IsInteger));
        Assert.Equal(LapVizInterpolation.Step, flat.Channels.Single(x => x.Name == "Gear").Interpolation);
        Assert.Same(file.Session, flat.Session);
        Assert.Single(flat.Events);
        Assert.Equal(2, flat.WrittenVersion);

        // A file already on one time base is returned as it is
        var single = new LapVizFile(Start);
        single.SetTime(new long[] { 0 });
        single.AddChannel(new LapVizChannel("RPM"), new[] { 1.0 });
        single.AddGroup("empty");
        Assert.Same(single, single.WithOneTimeBase());
    }

    [Fact]
    public void ResampleHoldsStepsAndInterpolatesTheRest()
    {
        long[] time = { 100, 200, 300, 400 };
        double[] values = { 1, 2, double.NaN, 4 };
        long[] target = { 50, 100, 150, 250, 350, 400, 450 };

        Assert.Equal(new[] { double.NaN, 1, 1.5, double.NaN, double.NaN, 4, double.NaN }, LapVizFile.Resample(time, values, target, LapVizInterpolation.Linear));
        Assert.Equal(new[] { double.NaN, 1, 1, 2, double.NaN, 4, double.NaN }, LapVizFile.Resample(time, values, target, LapVizInterpolation.Step));
        var none = LapVizFile.Resample(Array.Empty<long>(), Array.Empty<double>(), target, LapVizInterpolation.Linear);
        Assert.All(none, x => Assert.True(double.IsNaN(x)));
    }

    [Fact]
    public void NamesOfGroupsAndChannelsAreChecked()
    {
        var file = MultiRate();

        Assert.Throws<ArgumentException>(() => file.AddGroup("engine"));
        Assert.Throws<ArgumentException>(() => file.AddGroup("Engine")); // Lowercase only
        Assert.Throws<ArgumentException>(() => file.AddGroup("../x"));
        Assert.Throws<ArgumentException>(() => file.MainGroup.Name = "engine");
        var imu = file.AddGroup("imu");
        imu.SetTime(new long[] { 0 });
        Assert.Throws<ArgumentException>(() => imu.AddChannel(new LapVizChannel("rpm"), new[] { 1.0 })); // Unique in the file
    }

    [Fact]
    public void TheDataReaderReadsEveryGroup()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lvz");
        try
        {
            MultiRate().Write(path);
            var reader = new LapVizDataReader();
            Assert.True(reader.IsDataCompatible(path));
            reader.Load(path);

            Assert.Equal(new[] { "Latitude", "GPS Speed", "RPM", "Water Temp", "Gear", "Lateral" }, reader.GetTelemetryChannels());
            Assert.Equal(11, reader.GetSessionData().Single().TelemetryData.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AGroupWithoutSamplesKeepsItsChannels()
    {
        const string manifest = """
            {
              "format": "lapviz", "version": 3,
              "session": { "start": "2026-01-01T00:00:00Z" },
              "groups": [ { "name": "laps", "channels": [ { "name": "Speed", "type": "uint16", "scale": 0.01 } ] } ],
              "timing": [ { "source": "Phone", "laps": [ { "number": 1, "start": 0, "duration": 60.1 } ] } ]
            }
            """;
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("lapviz.json").Open()))
            writer.Write(manifest);

        stream.Position = 0;
        var file = LapVizFile.Read(stream);

        Assert.Equal("laps", file.MainGroup.Name);
        Assert.Equal(0.01, file.Channels[0].Scale);
        Assert.Empty(file.Values(0));
        Assert.Equal(60.1, file.PreferredTiming!.Laps[0].Duration);
    }
}
