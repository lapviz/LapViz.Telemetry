using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.IO;

/// <summary>
/// LapViz delimited telemetry writer (plain text or compressed, controlled by constructor flag).
///
/// Defaults:
/// - Streaming constructor: compressed by default (compressed=true).
/// - One-shot to disk via ITelemetryDataWriter.WriteAll(...): compressed by default (as required).
/// - One-shot to memory: ALWAYS compressed.
///
/// Text payload format (used both for plain text and inside the compressed entry):
///   #Format=LapViz Delimited Data
///   #Version=1
///   #CircuitCode=...        (optional)
///   #<custom header lines>  (optional)
///   #Fields=Field1,Field2,...
///   <timestampMs>,<v1>,<v2>,...
///   #Event=<tsMs>,<Type>,<Lap>,<Sector>,<TimeTicks>
/// </summary>
public class LapVizDataWriter : ITelemetryDataWriter, IDisposable
{
    // Header tokens
    private const string TOPHEADER = "#Format=LapViz Delimited Data";
    private const string VERSION = "#Version=1";
    private const string FIELDS_PREFIX = "#Fields=";
    private const string EVENT_PREFIX = "#Event=";
    private const string CIRCUIT_PREFX = "#CircuitCode=";

    // Streaming state (when using the filename constructor)
    private StreamWriter _writer;
    private readonly IList<string> _channels;

    // ZIP state for streaming mode
    private readonly bool _compressedMode;
    private FileStream _fileStream;   // outer file stream (plain or compressed container)
    private ZipArchive _archive;   // only when _compressedMode == true
    private Stream _archiveEntryStream;   // only when _compressedMode == true

    public LapVizDataWriter() { }

    /// <summary>
    /// Streaming constructor: opens the target and writes headers immediately.
    /// If <paramref name="compressed"/> is true (default), creates a ZIP with one entry "&lt;basename&gt;.lz".
    /// Otherwise, writes plain text to <paramref name="filename"/>.
    /// </summary>
    public LapVizDataWriter(string filename, IList<string> channels = null, IList<string> extraHeaders = null, bool compressed = true)
    {
        _compressedMode = compressed;

        _channels = (channels != null && channels.Any())
            ? new List<string>(channels)
            : new List<string> { "Latitude", "Longitude", "Speed", "Accuracy" };

        if (_compressedMode)
        {
            // Create archive and an entry for the text payload
            _fileStream = new FileStream(filename, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            _archive = new ZipArchive(_fileStream, ZipArchiveMode.Create, leaveOpen: true);

            var entryName = ComputeZipEntryName(filename, null);
            var entry = _archive.CreateEntry(entryName, CompressionLevel.Optimal);
            _archiveEntryStream = entry.Open();

            _writer = new StreamWriter(_archiveEntryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true);
        }
        else
        {
            // Plain text
            _fileStream = new FileStream(filename, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            _writer = new StreamWriter(_fileStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true);
        }

        WriteHeaders(_writer, _channels, extraHeaders, /*circuitCode*/ null);
        _writer.Flush();
    }

    /// <summary>
    /// ITelemetryDataWriter implementation: one-shot write to disk (compressed by default).
    /// </summary>
    public void WriteAll(DeviceSessionData session, string output, bool overwrite)
    {
        // Default behavior per your request: ALWAYS compressed for the interface method.
        WriteAll(session, output, overwrite, compressed: true);
    }

    /// <summary>
    /// Optional overload if you want to control compressed/plain at call site.
    /// </summary>
    public void WriteAll(DeviceSessionData session, string output, bool overwrite, bool compressed)
    {
        if (compressed)
        {
            using (var fs = new FileStream(output, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                var entryName = ComputeZipEntryName(output, session);
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using (var es = entry.Open())
                using (var writer = new StreamWriter(es, new UTF8Encoding(false), 1024, leaveOpen: false))
                {
                    WriteAllPayload(writer, session);
                }
            }
        }
        else
        {
            using (var writer = new StreamWriter(output, !overwrite, new UTF8Encoding(false)))
            {
                WriteAllPayload(writer, session);
            }
        }
    }

    /// <summary>
    /// One-shot write to an in-memory stream. ALWAYS returns a compressed archive containing one entry "&lt;basename&gt;.lz".
    /// Basename is derived from <see cref="DeviceSessionData.OriginalFilename"/> if available; otherwise "data".
    /// </summary>
    public MemoryStream WriteAll(DeviceSessionData session)
    {
        var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entryName = ComputeZipEntryName(null, session); // e.g., "session-name.lz" or "data.lz"
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var es = entry.Open())
            using (var writer = new StreamWriter(es, new UTF8Encoding(false), 1024, leaveOpen: false))
            {
                WriteAllPayload(writer, session);
            }
        }

        zipStream.Position = 0;
        return zipStream;
    }

    /// <summary>
    /// Streaming: append one telemetry data line (requires filename constructor).
    /// </summary>
    public void WriteData(ITelemetryData telemetryData)
    {
        EnsureWriter();
        _writer.WriteLine(BuildDataLine(telemetryData, _channels));
        _writer.Flush();
    }

    /// <summary>
    /// Streaming: append one event line (requires filename constructor).
    /// </summary>
    public void WriteEvent(SessionDataEvent sessionDataEvent)
    {
        EnsureWriter();
        _writer.WriteLine(BuildEventLine(sessionDataEvent));
        _writer.Flush();
    }

    /// <summary>Closes the underlying writer/archive (if open).</summary>
    public void Close()
    {
        Dispose();
    }

    public void Dispose()
    {
        // Dispose writer first (flushes and closes its owned stream)
        if (_writer != null)
        {
            _writer.Dispose();
            _writer = null;
        }

        // If streaming ZIP was used, close the entry, archive, and file
        if (_archiveEntryStream != null)
        {
            _archiveEntryStream.Dispose();
            _archiveEntryStream = null;
        }

        if (_archive != null)
        {
            _archive.Dispose();
            _archive = null;
        }

        if (_fileStream != null)
        {
            _fileStream.Dispose();
            _fileStream = null;
        }
    }

    /// <summary>
    /// Writes headers + all events + all data into the given <paramref name="writer"/>.
    /// Used by one-shot methods.
    /// </summary>
    private void WriteAllPayload(TextWriter writer, DeviceSessionData session)
    {
        var channels = (session.TelemetryChannels != null && session.TelemetryChannels.Any())
            ? session.TelemetryChannels
            : new List<string>();

        WriteHeaders(writer, channels, /*extraHeaders*/ null, session.CircuitCode);

        if (session.Events != null)
        {
            foreach (var evt in session.Events
                .OrderBy(e => (
                    e.LapNumber,
                    e.Type == SessionEventType.Sector ? 0 : 1,
                    e.Type == SessionEventType.Sector ? e.Sector : int.MaxValue)))
            {
                writer.WriteLine(BuildEventLine(evt));
            }
        }

        if (session.TelemetryData != null)
        {
            foreach (var d in session.TelemetryData)
                writer.WriteLine(BuildDataLine(d, channels));
        }
    }

    /// <summary>
    /// Computes the archive entry name: "&lt;basename&gt;.lz", where basename is taken from
    /// the container filename (if provided) or from session.OriginalFilename; falls back to "data".
    /// </summary>
    private static string ComputeZipEntryName(string containerFilename, DeviceSessionData session)
    {
        string baseName = null;

        if (!string.IsNullOrEmpty(containerFilename))
        {
            baseName = Path.GetFileNameWithoutExtension(containerFilename);
        }
        else if (session != null && !string.IsNullOrEmpty(session.OriginalFilename))
        {
            baseName = Path.GetFileNameWithoutExtension(session.OriginalFilename);
        }

        if (string.IsNullOrEmpty(baseName))
            baseName = "data";

        return baseName + ".lz";
    }

    /// <summary>
    /// Writes the standard headers to the given <paramref name="writer"/>.
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="channels"></param>
    /// <param name="extraHeaders"></param>
    /// <param name="circuitCode"></param>
    private static void WriteHeaders(TextWriter writer, IList<string> channels, IEnumerable<string> extraHeaders, string circuitCode)
    {
        writer.WriteLine(TOPHEADER);
        writer.WriteLine(VERSION);

        if (!string.IsNullOrEmpty(circuitCode))
            writer.WriteLine(CIRCUIT_PREFX + circuitCode);

        if (extraHeaders != null)
        {
            foreach (var h in extraHeaders)
            {
                if (!string.IsNullOrWhiteSpace(h))
                    writer.WriteLine("#" + h.Trim());
            }
        }

        writer.WriteLine(FIELDS_PREFIX + string.Join(",", channels));
    }

    /// <summary>
    /// Builds one event line.
    /// </summary>
    /// <param name="evt"></param>
    /// <returns></returns>
    private static string BuildEventLine(SessionDataEvent evt)
    {
        // "#Event=tsMs,Type,Lap,Sector,TimeTicks"
        return string.Concat(
            EVENT_PREFIX,
            evt.Timestamp.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), ",",
            evt.Type.ToString(), ",",
            evt.LapNumber.ToString(CultureInfo.InvariantCulture), ",",
            evt.Sector.ToString(CultureInfo.InvariantCulture), ",",
            evt.Time.Ticks.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Builds one telemetry data line.
    /// </summary>
    /// <param name="telemetryData"></param>
    /// <param name="channels"></param>
    /// <returns></returns>
    private static string BuildDataLine(ITelemetryData telemetryData, IList<string> channels)
    {
        // "tsMs,v1,v2,..."
        var sb = new StringBuilder(64 + channels.Count * 16);
        sb.Append(telemetryData.Timestamp.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        sb.Append(',');

        var data = telemetryData.Data ?? (IList<double?>)Array.Empty<double?>();
        int n = channels.Count;

        for (int i = 0; i < n; i++)
        {
            if (i < data.Count && data[i].HasValue)
                sb.Append(data[i].Value.ToString(CultureInfo.InvariantCulture));

            if (i < n - 1)
                sb.Append(',');
        }
        return sb.ToString();
    }

    private void EnsureWriter()
    {
        if (_writer == null)
            throw new InvalidOperationException("Writer not initialized. Use the filename constructor for streaming writes, or use WriteAll(...) methods.");
    }
}
