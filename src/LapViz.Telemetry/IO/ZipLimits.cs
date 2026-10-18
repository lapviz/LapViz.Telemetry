using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace LapViz.Telemetry.IO;

/// <summary>
/// Bounds on what is decompressed from the ZIP archives read by the library (LapViz files, zipped exports of the
/// loggers). A small archive may expand to gigabytes (ZIP bomb): an application that reads files from anybody would
/// run out of memory or disk.
/// </summary>
public static class ZipLimits
{
    /// <summary>Most bytes decompressed from one entry, and from one archive in all. 512 MB by default.</summary>
    public static long MaxUncompressedBytes { get; set; } = 512L * 1024 * 1024;

    /// <summary>Most entries extracted from one archive. 1000 by default.</summary>
    public static int MaxEntries { get; set; } = 1000;

    /// <summary>The bytes of an entry, at most <paramref name="limit"/> (<see cref="MaxUncompressedBytes"/> by default).</summary>
    /// <exception cref="InvalidDataException">The entry is larger.</exception>
    public static byte[] ReadAll(ZipArchiveEntry entry, long? limit = null)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        var max = Math.Min(limit ?? MaxUncompressedBytes, MaxUncompressedBytes);
        if (entry.Length > max)
            throw new InvalidDataException(entry.FullName + " is larger than " + max + " bytes");

        using (var input = entry.Open())
        using (var buffer = new MemoryStream((int)Math.Min(Math.Max(entry.Length, 0), max)))
        {
            Copy(input, buffer, max, entry.FullName);
            return buffer.ToArray();
        }
    }

    /// <summary>
    /// Extracts the files of an archive into <paramref name="directory"/>, flat (the folders of the archive are dropped:
    /// an entry never lands outside), within <see cref="MaxEntries"/> and <see cref="MaxUncompressedBytes"/> in all.
    /// Returns the paths written; on an error, the files written so far are left to the caller's cleanup.
    /// </summary>
    /// <exception cref="InvalidDataException">The archive holds too many entries or bytes.</exception>
    public static IList<string> ExtractFlat(string archive, string directory)
    {
        var written = new List<string>();
        Directory.CreateDirectory(directory);
        long total = 0;
        using (var zip = ZipFile.OpenRead(archive))
        {
            foreach (var entry in zip.Entries)
            {
                var name = Path.GetFileName(entry.FullName.Replace('\\', '/'));
                if (string.IsNullOrEmpty(name))
                    continue; // A folder

                if (written.Count >= MaxEntries)
                    throw new InvalidDataException("The archive holds more than " + MaxEntries + " files");

                var path = Path.Combine(directory, name);
                for (var i = 1; File.Exists(path); i++)
                    path = Path.Combine(directory, Path.GetFileNameWithoutExtension(name) + "_" + i + Path.GetExtension(name));

                using (var input = entry.Open())
                using (var output = File.Create(path))
                {
                    written.Add(path);
                    total += Copy(input, output, MaxUncompressedBytes - total, archive);
                }
            }
        }

        return written;
    }

    private static long Copy(Stream input, Stream output, long max, string name)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            copied += read;
            if (copied > max)
                throw new InvalidDataException(name + " expands to more than " + MaxUncompressedBytes + " bytes");

            output.Write(buffer, 0, read);
        }

        return copied;
    }
}
