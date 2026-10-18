using System.IO.Compression;
using System.Text;
using LapViz.Telemetry.IO;

namespace LapViz.Telemetry.Tests.IO;

/// <summary>Changes the limits of <see cref="ZipLimits"/> (static): never in parallel with the other tests.</summary>
[CollectionDefinition(nameof(ZipLimitsTests), DisableParallelization = true)]
public class ZipLimitsCollection
{
}

/// <summary>ZIP bombs: a small archive that expands to gigabytes must not exhaust memory or disk.</summary>
[Collection(nameof(ZipLimitsTests))]
public class ZipLimitsTests
{
    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = zip.CreateEntry(name, CompressionLevel.SmallestSize).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    [Fact]
    public void BinarySamplesLargerThanTheManifestSaysAreNotDecompressed()
    {
        using var valid = new MemoryStream();
        LapVizFileTests.Sample().Write(valid);
        using var original = new ZipArchive(new MemoryStream(valid.ToArray()), ZipArchiveMode.Read);
        byte[] manifest;
        using (var reader = new MemoryStream())
        {
            original.GetEntry(LapVizFile.ManifestEntry)!.Open().CopyTo(reader);
            manifest = reader.ToArray();
        }

        // The manifest announces 3 samples, the entry holds 8 MB of zeros (a few kB compressed)
        var bomb = Zip((LapVizFile.ManifestEntry, manifest), ("samples.bin", new byte[8 * 1024 * 1024]));
        var error = Assert.Throws<InvalidDataException>(() => LapVizFile.Read(new MemoryStream(bomb)));
        Assert.Contains("larger than", error.Message);
    }

    [Fact]
    public void AnEntryOverTheLimitIsRefusedWhileItIsRead()
    {
        var previous = ZipLimits.MaxUncompressedBytes;
        try
        {
            ZipLimits.MaxUncompressedBytes = 1024 * 1024;
            var bomb = Zip(("data.csv", Encoding.ASCII.GetBytes(new string('0', 4 * 1024 * 1024))));
            using var zip = new ZipArchive(new MemoryStream(bomb), ZipArchiveMode.Read);
            Assert.Throws<InvalidDataException>(() => ZipLimits.ReadAll(zip.Entries[0]));

            var small = Zip(("data.csv", Encoding.ASCII.GetBytes("time,speed\n0,12\n")));
            using var smallZip = new ZipArchive(new MemoryStream(small), ZipArchiveMode.Read);
            Assert.Equal(16, ZipLimits.ReadAll(smallZip.Entries[0]).Length);
        }
        finally
        {
            ZipLimits.MaxUncompressedBytes = previous;
        }
    }

    [Fact]
    public void ExtractionIsFlatAndBounded()
    {
        var previous = (ZipLimits.MaxUncompressedBytes, ZipLimits.MaxEntries);
        var folder = Path.Combine(Path.GetTempPath(), "lapviz-zip-tests-" + Guid.NewGuid().ToString("N"));
        var archive = folder + ".zip";
        try
        {
            File.WriteAllBytes(archive, Zip(("../../escape.csv", new byte[] { 1 }), ("run/lap.csv", new byte[] { 2 }), ("lap.csv", new byte[] { 3 })));
            var written = ZipLimits.ExtractFlat(archive, folder);
            Assert.Equal(3, written.Count);
            Assert.All(written, path => Assert.Equal(folder, Path.GetDirectoryName(path)));

            ZipLimits.MaxEntries = 2;
            Assert.Throws<InvalidDataException>(() => ZipLimits.ExtractFlat(archive, folder + "-entries"));

            ZipLimits.MaxEntries = 1000;
            ZipLimits.MaxUncompressedBytes = 1024 * 1024;
            File.WriteAllBytes(archive, Zip(("a.csv", new byte[600 * 1024]), ("b.csv", new byte[600 * 1024])));
            Assert.Throws<InvalidDataException>(() => ZipLimits.ExtractFlat(archive, folder + "-bytes"));
        }
        finally
        {
            (ZipLimits.MaxUncompressedBytes, ZipLimits.MaxEntries) = previous;
            foreach (var path in new[] { folder, folder + "-entries", folder + "-bytes" })
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }

            File.Delete(archive);
        }
    }
}
