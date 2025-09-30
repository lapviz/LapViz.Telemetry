using System.Globalization;
using System.IO.Compression;
using System.Text;
using LapViz.Telemetry.Domain;
using LapViz.Telemetry.IO;

namespace LapViz.Telemetry.Tests.Services;

public class DelimitedDataReaderTests
{
    private static string AssetCsvPath =>
        Path.Combine(AppContext.BaseDirectory, "runs", "MariembourgFreeTests.csv");

    private static string NewTempFile(string ext = ".csv")
        => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{ext}");

    private static void WriteAllText(string path, string content, Encoding enc = null)
    {
        enc ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(path, content, enc);
    }

    private static DelimitedDataReaderConfiguration BaseConfig(
        char delimiter = ',',
        string channelsSignature = "\"Time",
        string device = "Test")
        => new DelimitedDataReaderConfiguration
        {
            Delimiter = delimiter,
            ChannelsSignature = channelsSignature,
            TelemetryDevice = device,
            SpeedConversionFactor = 1.0,
            FileNameInclude = "*.csv"
        };

    private static DeviceSessionData LoadWith(DelimitedDataReaderConfiguration cfg, string file)
    {
        var rdr = new DelimitedDataReader(cfg);
        rdr.Load(file);
        return rdr.GetSessionData().First();
    }

    [Fact]
    public void EnsureCsvExists()
    {
        Assert.True(File.Exists(AssetCsvPath), $"CSV not found at {AssetCsvPath}");
    }

    public sealed class RealCsv
    {
        [Fact]
        public void GetTelemetryChannels_And_SessionData_From_MariembourgCsv()
        {
            var cfg = BaseConfig();
            var rdr = new DelimitedDataReader(cfg);
            rdr.Load(AssetCsvPath);

            // Channels should be detected and non-empty
            var channels = rdr.GetTelemetryChannels();
            Assert.NotNull(channels);
            Assert.NotEmpty(channels);

            // Full session parse
            var session = rdr.GetSessionData().First();
            Assert.NotNull(session);
            Assert.NotEmpty(session.TelemetryData);
            Assert.Equal(cfg.TelemetryDevice, session.Generator);

            // Data count aligns with channels
            var first = session.TelemetryData.First() as GeoTelemetryData;
            Assert.NotNull(first);
            Assert.Equal(session.TelemetryChannels.Count, first.Data.Count);

            // Core geospatial fields should be populated
            Assert.True(first.Latitude > 0);
            Assert.True(first.Longitude > 0);

            // MaxSpeed computed (>= 0). We can’t assume a specific positive value for all datasets.
            Assert.True(session.MaxSpeed >= 0);
        }

        [Fact]
        public void Data_And_Channels_Stay_Aligned()
        {
            var cfg = BaseConfig();
            var session = LoadWith(cfg, AssetCsvPath);

            // Spot-check several rows for alignment with channels
            foreach (var td in session.TelemetryData.Take(10).Cast<GeoTelemetryData>())
            {
                Assert.Equal(session.TelemetryChannels.Count, td.Data.Count);
            }
        }
    }

    public sealed class ChannelsParsing
    {
        [Fact]
        public void ParseChannels_Handles_Quotes_And_Embedded_Delimiters()
        {
            var cfg = BaseConfig();
            var rdr = new DelimitedDataReader(cfg);

            // Channel names include quotes and a comma inside a quoted token
            var header = "\"Time\",\"Latitude\",\"Longitude\",\"Speed\",\"Note, With Comma\"";
            var parsed = rdr.ParseChannels(header);

            Assert.Equal(new[] { "Time", "Latitude", "Longitude", "Speed", "Note, With Comma" }, parsed);
        }
    }

    public sealed class Timestamps
    {
        [Fact]
        public void EpochMilliseconds_Are_Detected_Without_Rounding()
        {
            // ms >= 1e11 -> treat as milliseconds since epoch
            var cfg = BaseConfig(channelsSignature: "Time");
            var tmp = NewTempFile();
            try
            {
                var epoch0 = DateTimeOffset.FromUnixTimeMilliseconds(0);
                var t0ms = 1735689600000.123; // 2025-01-01 00:00:00.123Z (ms with fractional)
                var t1ms = 1735689600456.789;

                var csv =
                    "Time,Latitude,Longitude,Speed\n" +
                    $"{t0ms.ToString(CultureInfo.InvariantCulture)},50.1,5.1,10\n" +
                    $"{t1ms.ToString(CultureInfo.InvariantCulture)},50.2,5.2,20\n";

                WriteAllText(tmp, csv);

                var session = LoadWith(cfg, tmp);
                var s0 = (GeoTelemetryData)session.TelemetryData[0];
                var s1 = (GeoTelemetryData)session.TelemetryData[1];

                // Expect exact ms (down to fractional ms preserved by AddMilliseconds(double))
                Assert.Equal(epoch0.AddMilliseconds(t0ms), s0.Timestamp);
                Assert.Equal(epoch0.AddMilliseconds(t1ms), s1.Timestamp);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        [Fact]
        public void EpochSeconds_Are_Detected_Without_Rounding()
        {
            // < 1e11 -> treat as seconds since epoch
            var cfg = BaseConfig(channelsSignature: "Time");
            var tmp = NewTempFile();
            try
            {
                var epoch0 = DateTimeOffset.FromUnixTimeMilliseconds(0);
                var t0s = 1735689600.123; // 2025-01-01 00:00:00.123Z (seconds with fractional)
                var t1s = 1735689601.999;

                var csv =
                    "Time,Latitude,Longitude,Speed\n" +
                    $"{t0s.ToString(CultureInfo.InvariantCulture)},50.1,5.1,10\n" +
                    $"{t1s.ToString(CultureInfo.InvariantCulture)},50.2,5.2,20\n";

                WriteAllText(tmp, csv);

                var session = LoadWith(cfg, tmp);
                var s0 = (GeoTelemetryData)session.TelemetryData[0];
                var s1 = (GeoTelemetryData)session.TelemetryData[1];

                Assert.Equal(epoch0.AddSeconds(t0s), s0.Timestamp);
                Assert.Equal(epoch0.AddSeconds(t1s), s1.Timestamp);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        [Fact]
        public void ISO8601_Timestamp_Overrides_Numeric_When_Configured()
        {
            // When TimestampFormat == ISO8601, the raw text at _timestampIndex is parsed instead
            var cfg = BaseConfig(channelsSignature: "Time");
            cfg.TimestampFormat = TimestampFormat.ISO8601;

            var tmp = NewTempFile();
            try
            {
                var iso0 = "2025-01-01T12:34:56.789Z";
                var iso1 = "2025-01-01T12:34:57.001+00:00";

                var csv =
                    "Time,Latitude,Longitude,Speed\n" +
                    $"{iso0},50.1,5.1,10\n" +
                    $"{iso1},50.2,5.2,20\n";

                WriteAllText(tmp, csv);

                var session = LoadWith(cfg, tmp);
                var s0 = (GeoTelemetryData)session.TelemetryData[0];
                var s1 = (GeoTelemetryData)session.TelemetryData[1];

                Assert.Equal(DateTimeOffset.Parse(iso0, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), s0.Timestamp);
                Assert.Equal(DateTimeOffset.Parse(iso1, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), s1.Timestamp);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        [Fact]
        public void Synthesized_Timestamps_When_No_Time_Column_Use_DataResolution()
        {
            var cfg = BaseConfig(channelsSignature: "Lat");
            cfg.DataResolution = 50; // 50 Hz -> 20 ms step

            var tmp = NewTempFile();
            try
            {
                var csv =
                    "Latitude,Longitude,Speed\n" +
                    "50.0001,5.0001,10\n" +
                    "50.0002,5.0002,12\n" +
                    "50.0003,5.0003,14\n";

                WriteAllText(tmp, csv);

                var session = LoadWith(cfg, tmp);
                var s0 = (GeoTelemetryData)session.TelemetryData[0];
                var s1 = (GeoTelemetryData)session.TelemetryData[1];
                var s2 = (GeoTelemetryData)session.TelemetryData[2];

                // We cannot assert absolute time (it starts at Now), but step should be ~20ms
                var d1 = s1.Timestamp - s0.Timestamp;
                var d2 = s2.Timestamp - s1.Timestamp;
                Assert.InRange(Math.Abs((d1 - TimeSpan.FromMilliseconds(20)).TotalMilliseconds), 0, 5);
                Assert.InRange(Math.Abs((d2 - TimeSpan.FromMilliseconds(20)).TotalMilliseconds), 0, 5);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
    }

    public sealed class MappingAndIgnore
    {
        [Fact]
        public void ColumnsToIgnore_Are_Removed_And_Data_Aligns_With_Kept_Channels()
        {
            var cfg = BaseConfig(channelsSignature: "Time");
            cfg.ColumnsToIgnore = new List<string> { "IgnoreMe" };

            var tmp = NewTempFile();
            try
            {
                // Note: "IgnoreMe" is in the header and in data; it must not appear in TelemetryChannels
                var csv =
                    "Time,Latitude,IgnoreMe,Longitude,Speed\n" +
                    "1735689600000,50.1,999,5.1,10\n" +
                    "1735689600100,50.2,888,5.2,20\n";

                WriteAllText(tmp, csv);

                var rdr = new DelimitedDataReader(cfg);
                rdr.Load(tmp);

                var channels = rdr.GetTelemetryChannels();
                Assert.DoesNotContain("IgnoreMe", channels);

                var session = rdr.GetSessionData().First();
                Assert.Equal(channels, session.TelemetryChannels);

                foreach (var geo in session.TelemetryData.Cast<GeoTelemetryData>())
                    Assert.Equal(channels.Count, geo.Data.Count);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        [Fact]
        public void ChannelNameMapping_And_Speed_Conversion_Applies()
        {
            // Map common names to format-specific names and convert Speed (e.g., m/s -> km/h)
            var cfg = BaseConfig(channelsSignature: "TS");
            cfg.SpeedConversionFactor = 3.6; // m/s to km/h

            cfg.ChannelNameMapping = new List<DelimitedDataReaderChannelMap>
            {
                new DelimitedDataReaderChannelMap { CommonChannelName = "Time",     FormatChannelName = "TS" },
                new DelimitedDataReaderChannelMap { CommonChannelName = "Latitude", FormatChannelName = "Lat" },
                new DelimitedDataReaderChannelMap { CommonChannelName = "Longitude",FormatChannelName = "Lon" },
                new DelimitedDataReaderChannelMap { CommonChannelName = "Speed",    FormatChannelName = "Vms" },
            };

            var tmp = NewTempFile();
            try
            {
                var csv =
                    "TS,Lat,Lon,Vms\n" +
                    "1735689600,50.0,5.0,10\n" +  // 10 m/s -> 36 km/h
                    "1735689601,50.1,5.1,12\n";   // 12 m/s -> 43.2 km/h

                WriteAllText(tmp, csv);

                var session = LoadWith(cfg, tmp);
                var g0 = (GeoTelemetryData)session.TelemetryData[0];
                var g1 = (GeoTelemetryData)session.TelemetryData[1];

                Assert.NotNull(g0.Speed);
                Assert.Equal(36.0, g0.Speed!.Value, 5);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
    }

    public sealed class ZipInput
    {
        [Fact]
        public void Zip_Is_Expanded_And_Parsed_Using_Include_And_Exclude()
        {
            var cfg = BaseConfig(channelsSignature: "Time");
            cfg.FileNameInclude = "*.csv";
            cfg.FileNameExclude = "ignore"; // exclude the one with "ignore" in filename

            // Build two small CSVs and zip them; only one should be parsed.
            var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            var good = Path.Combine(tmpDir, "data.csv");
            var bad = Path.Combine(tmpDir, "ignore_this.csv");

            var csvGood =
                "Time,Latitude,Longitude\n" +
                "1735689600000,50.1,5.1\n";
            var csvBad =
                "Time,Latitude,Longitude\n" +
                "1735689600001,99.9,99.9\n";

            WriteAllText(good, csvGood);
            WriteAllText(bad, csvBad);

            var zipPath = NewTempFile(".zip");
            try
            {
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(good, Path.GetFileName(good));
                    zip.CreateEntryFromFile(bad, Path.GetFileName(bad));
                }

                var session = LoadWith(cfg, zipPath);
                var first = (GeoTelemetryData)session.TelemetryData.Single();

                // Ensure we parsed the "good" entry and not the ignored one
                Assert.Equal(50.1, first.Latitude, 9);
                Assert.Equal(5.1, first.Longitude, 9);
            }
            finally
            {
                try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); } catch { }
                if (File.Exists(zipPath)) File.Delete(zipPath);
            }
        }
    }

    public sealed class GatingAndFilters
    {
        [Fact]
        public void DataStartsAfter_And_NonDataLineMatches_Skip_Preamble_And_Comments()
        {
            var cfg = BaseConfig(channelsSignature: "Time");
            cfg.DataStartsAfter = "BEGIN_DATA";
            cfg.NonDataLineMatches = new List<string> { "META", "#", "//" };

            var tmp = NewTempFile();
            try
            {
                var csv =
                    "Some header\n" +
                    "META something\n" +
                    "BEGIN_DATA now\n" +
                    "META skip this too\n" +
                    "Time,Latitude,Longitude\n" +   // header
                    "1735689600000,50.1,5.1\n" +    // data
                    "# a comment\n" +
                    "1735689600100,50.2,5.2\n";     // data

                WriteAllText(tmp, csv);

                var session = LoadWith(cfg, tmp);
                Assert.Equal(2, session.TelemetryData.Count);
                var g0 = (GeoTelemetryData)session.TelemetryData[0];
                var g1 = (GeoTelemetryData)session.TelemetryData[1];
                Assert.Equal(50.1, g0.Latitude, 9);
                Assert.Equal(50.2, g1.Latitude, 9);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
    }

    public sealed class Compatibility
    {
        [Fact]
        public void IsDataCompatible_True_When_FileSignature_Matches_First_Lines()
        {
            var cfg = BaseConfig(channelsSignature: "Time");
            cfg.FileSignature = "MYFMT";

            var tmp = NewTempFile();
            try
            {
                var csv =
                    "MYFMT,whatever\n" +
                    "another line\n" +
                    "Time,Latitude,Longitude\n" +
                    "1735689600000,50.1,5.1\n";
                WriteAllText(tmp, csv);

                var rdr = new DelimitedDataReader(cfg);
                Assert.True(rdr.IsDataCompatible(tmp));
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        [Fact]
        public void IsDataCompatible_False_When_Signature_Not_Found()
        {
            var cfg = BaseConfig(channelsSignature: "Time");
            cfg.FileSignature = "XYZSIG";

            var tmp = NewTempFile();
            try
            {
                var csv =
                    "NOTSIG,whatever\n" +
                    "another line\n" +
                    "Time,Latitude,Longitude\n" +
                    "1735689600000,50.1,5.1\n";
                WriteAllText(tmp, csv);

                var rdr = new DelimitedDataReader(cfg);
                Assert.False(rdr.IsDataCompatible(tmp));
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
    }
}
