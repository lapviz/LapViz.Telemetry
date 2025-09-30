using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Reflection;
using LapViz.Telemetry.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LapViz.Telemetry.IO;

/// <summary>
/// Factory that discovers and instantiates <see cref="ITelemetryDataReader"/> implementations via MEF.
/// </summary>
public sealed class TelemetryDataReaderFactory : IDisposable
{
    // Thread-safe singleton
    private static readonly Lazy<TelemetryDataReaderFactory> _lazy =
        new Lazy<TelemetryDataReaderFactory>(() => new TelemetryDataReaderFactory(new NullLogger<TelemetryDataReaderFactory>()));

    /// <summary>Global instance using a NullLogger by default.</summary>
    public static TelemetryDataReaderFactory Instance => _lazy.Value;

    private readonly ILogger<TelemetryDataReaderFactory> _logger;

    private AggregateCatalog _catalog;
    private CompositionContainer _container;

    /// <summary>Readers imported from MEF (non-shared; new instance each time).</summary>
    [ImportMany(RequiredCreationPolicy = CreationPolicy.NonShared)]
    public IEnumerable<ITelemetryDataReader> Readers;

    /// <summary>
    /// Create a factory. Call <see cref="LoadReaderExtensions"/> to scan plugins.
    /// </summary>
    public TelemetryDataReaderFactory(ILogger<TelemetryDataReaderFactory> logger)
    {
        _logger = logger ?? (ILogger<TelemetryDataReaderFactory>)new NullLogger<TelemetryDataReaderFactory>();
        _catalog = new AggregateCatalog();
        // We do not load by default here; Instance uses LoadReaderExtensions() to keep old behavior
        LoadReaderExtensions();
    }

    /// <summary>
    /// Discover readers from default plugin folders and compose imports.
    /// Safe to call multiple times; will recompose.
    /// </summary>
    public void LoadReaderExtensions()
    {
        // Reset previous composition if any
        DisposeContainerOnly();

        _catalog = new AggregateCatalog();
        var pluginDirectories = new List<string>
        {
            AppDomain.CurrentDomain.BaseDirectory,
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins")
        };

        foreach (var directory in pluginDirectories)
        {
            try
            {
                if (!Directory.Exists(directory)) continue;

                var dllFiles = Directory.GetFiles(directory, "*.dll");
                foreach (var dll in dllFiles)
                {
                    var fileName = Path.GetFileName(dll);

                    // Skip known incompatible bits (e.g., MAUI UI libs)
                    if (fileName.StartsWith("Microsoft.Maui", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Skipping incompatible plugin DLL: {FileName}", fileName);
                        continue;
                    }

                    try
                    {
                        var asmCatalog = new AssemblyCatalog(dll);

                        // Force type load now; if it fails, we skip this DLL
                        var _ = asmCatalog.Assembly.GetTypes();

                        _catalog.Catalogs.Add(asmCatalog);
                    }
                    catch (ReflectionTypeLoadException rex)
                    {
                        _logger.LogError(rex, "Failed to load types from {FileName}. Skipping.", fileName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error loading {FileName}. Skipping.", fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning plugin directory {Dir}", directory);
            }
        }

        _container = new CompositionContainer(_catalog);

        try
        {
            _container.ComposeParts(this);

            if (Readers != null)
            {
                foreach (var reader in Readers)
                    _logger.LogInformation("Loaded telemetry reader: {ReaderType}", reader.GetType().FullName);
            }
        }
        catch (CompositionException cex)
        {
            _logger.LogError(cex, "Composition error while loading telemetry readers.");
            throw;
        }
    }

    /// <summary>
    /// Try to get a reader compatible with the given file.
    /// </summary>
    public bool TryGetReader(string filename, out ITelemetryDataReader readerInstance)
    {
        readerInstance = null;
        if (string.IsNullOrWhiteSpace(filename))
        {
            _logger.LogWarning("TryGetReader called with empty filename.");
            return false;
        }

        if (Readers == null)
        {
            _logger.LogWarning("No readers available. Did you call LoadReaderExtensions()?");
            return false;
        }

        foreach (var reader in Readers)
        {
            try
            {
                if (reader.IsDataCompatible(filename))
                {
                    // Create a fresh instance (NonShared import gives us a new one already,
                    // but we use Activator to avoid reusing imported instances across calls).
                    var type = reader.GetType();
                    var ctor = type.GetConstructor(Type.EmptyTypes);
                    if (ctor == null)
                    {
                        _logger.LogError("Reader {ReaderType} lacks a public parameterless constructor.", type.FullName);
                        continue;
                    }

                    var instance = (ITelemetryDataReader)Activator.CreateInstance(type);
                    readerInstance = instance;
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Compatibility probe failed for reader {ReaderType} with file {File}", reader.GetType().FullName, filename);
            }
        }

        return false;
    }

    /// <summary>
    /// Get a reader compatible with the given file, or throw if none found.
    /// </summary>
    public ITelemetryDataReader GetReader(string filename)
    {
        ITelemetryDataReader instance;
        if (TryGetReader(filename, out instance))
            return instance;

        throw new InvalidOperationException("No telemetry reader found for file: " + filename);
    }

    /// <summary>
    /// Try to choose a reader from an explicit list (e.g., unit tests) for a file.
    /// </summary>
    public bool TryGetReader(string filename, IEnumerable<ITelemetryDataReader> readers, out ITelemetryDataReader readerInstance)
    {
        readerInstance = null;
        if (string.IsNullOrWhiteSpace(filename) || readers == null)
            return false;

        foreach (var reader in readers)
        {
            try
            {
                if (reader.IsDataCompatible(filename))
                {
                    var type = reader.GetType();
                    var ctor = type.GetConstructor(Type.EmptyTypes);
                    if (ctor == null)
                    {
                        _logger.LogError("Reader {ReaderType} lacks a public parameterless constructor.", type.FullName);
                        continue;
                    }

                    readerInstance = (ITelemetryDataReader)Activator.CreateInstance(type);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Compatibility probe failed for injected reader {ReaderType} with file {File}", reader.GetType().FullName, filename);
            }
        }

        return false;
    }

    /// <summary>
    /// Dispose container and catalogs.
    /// </summary>
    public void Dispose()
    {
        DisposeContainerOnly();

        if (_catalog != null)
        {
            try { _catalog.Dispose(); } catch { }
            _catalog = null;
        }
    }

    private void DisposeContainerOnly()
    {
        if (_container != null)
        {
            try { _container.Dispose(); } catch { }
            _container = null;
        }
    }
}
