using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.IO;

/// <summary>
/// Base class for telemetry data readers that load data from the file system.
/// Provides a common pattern for loading a file, checking compatibility,
/// extracting telemetry channels, and computing file hashes.
/// </summary>
public abstract class FileSystemTelemetryDataReader : ITelemetryDataReader, IDisposable
{
    /// <summary>
    /// Path to the file being processed.
    /// Set by <see cref="Load"/>.
    /// </summary>
    protected string _filename;

    /// <summary>
    /// Gets the list of telemetry channel names available in the file.
    /// Override in a subclass to return actual channels.
    /// </summary>
    public virtual IList<string> GetTelemetryChannels()
    {
        return new List<string>();
    }

    /// <summary>
    /// Computes the MD5 hash of the loaded file.
    /// Can be used to detect duplicates or verify integrity.
    /// </summary>
    /// <returns>A lowercase hex string of the MD5 hash.</returns>
    public virtual string GetHash()
    {
        using (var md5 = MD5.Create())
        using (var stream = File.OpenRead(_filename))
        {
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>
    /// Reads session data from the file.
    /// Override in subclasses to parse the file and return session data objects.
    /// </summary>
    /// <returns>List of <see cref="DeviceSessionData"/> parsed from the file, or null by default.</returns>
    public virtual IList<DeviceSessionData> GetSessionData()
    {
        return null;
    }

    /// <summary>
    /// Loads a file for reading.
    /// Stores the filename internally and returns this reader instance for chaining.
    /// </summary>
    /// <param name="filename">The path to the file to load.</param>
    /// <returns>This <see cref="ITelemetryDataReader"/> instance.</returns>
    public virtual ITelemetryDataReader Load(string filename)
    {
        _filename = filename;
        return this;
    }

    /// <summary>
    /// Determines whether a given file is compatible with this reader.
    /// Override in subclasses with logic to check file format or extension.
    /// </summary>
    /// <param name="filename">The path of the file to check.</param>
    /// <returns>True if compatible, false otherwise (default: false).</returns>
    public virtual bool IsDataCompatible(string filename)
    {
        return false;
    }

    /// <summary>
    /// Disposes of any resources used by the reader.
    /// No resources to release by default, but subclasses may override.
    /// </summary>
    public void Dispose()
    {
        // No-op by default
    }
}
