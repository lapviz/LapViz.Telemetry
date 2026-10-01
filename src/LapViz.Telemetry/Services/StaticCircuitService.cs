using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Services;

/// <summary>
/// ICircuitService implementation backed by an in-memory, static list of circuits
/// (the built-in dataset, or circuits supplied by the caller). Detection is bounding-box based.
/// </summary>
public class StaticCircuitService : ICircuitService
{
    /// <summary>In-memory list of circuits (immutable after construction).</summary>
    private readonly IList<CircuitConfiguration> _circuits;

    /// <summary>Index for fast case-insensitive lookups by code.</summary>
    private readonly Dictionary<string, CircuitConfiguration> _byCode;

    /// <summary>Reported last-update time (kept for compatibility with older clients).</summary>
    private readonly DateTimeOffset _updated =
        new DateTimeOffset(2023, 6, 21, 10, 30, 25, TimeSpan.Zero);

    private static readonly StringComparer CodeComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Creates a service with the whole built-in dataset.
    /// </summary>
    public StaticCircuitService()
    {
        _circuits = InitializeCircuits();
        _byCode = BuildIndex(_circuits);
    }

    /// <summary>
    /// Creates a service with a single circuit (useful for tests).
    /// </summary>
    public StaticCircuitService(CircuitConfiguration circuit)
    {
        if (circuit == null) throw new ArgumentNullException(nameof(circuit));
        _circuits = new List<CircuitConfiguration> { circuit };
        _byCode = BuildIndex(_circuits);
    }

    /// <summary>
    /// Creates a service with an explicit list of circuits (e.g., for DI).
    /// </summary>
    public StaticCircuitService(IEnumerable<CircuitConfiguration> circuits)
    {
        if (circuits == null) throw new ArgumentNullException(nameof(circuits));
        _circuits = circuits as IList<CircuitConfiguration> ?? circuits.ToList();
        _byCode = BuildIndex(_circuits);
    }

    /// <summary>
    /// Fast case-insensitive lookup by circuit code.
    /// </summary>
    public Task<CircuitConfiguration> GetByCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Task.FromResult<CircuitConfiguration>(null);

        return Task.FromResult(_byCode.TryGetValue(code, out var circuit) ? circuit : null);
    }

    /// <summary>
    /// Detect the circuit that contains the given GPS point, based on its bounding box.
    /// Returns the first match (order of _circuits matters if boxes overlap).
    /// </summary>
    public Task<CircuitConfiguration> Detect(GeoTelemetryData geoLocation)
    {
        if (geoLocation == null)
            return Task.FromResult<CircuitConfiguration>(null);

        CircuitConfiguration found = null;

        foreach (var circuit in _circuits)
        {
            if (circuit?.BoundingBox != null && circuit.BoundingBox.IsWithinBox(geoLocation))
            {
                found = circuit;
                break;
            }
        }

        return Task.FromResult(found);
    }

    /// <summary>
    /// Dummy sync: immediately reports 100% (kept for API compatibility).
    /// </summary>
    public Task Sync(double lat, double lon, int radius = 200)
    {
        OnSyncProgress(new CircuitSyncProgress { Progress = 1 });
        return Task.CompletedTask;
    }

    public event EventHandler<CircuitSyncProgress> SyncProgress;
    protected virtual void OnSyncProgress(CircuitSyncProgress e)
        => SyncProgress?.Invoke(this, e);

    /// <summary>
    /// Returns a fixed "last updated" timestamp, matching previous behavior.
    /// </summary>
    public DateTimeOffset Updated => _updated;

    /// <summary>
    /// Builds a case-insensitive dictionary keyed by CircuitConfiguration.Code.
    /// Missing/empty codes are ignored.
    /// </summary>
    private static Dictionary<string, CircuitConfiguration> BuildIndex(IEnumerable<CircuitConfiguration> circuits)
    {
        var dict = new Dictionary<string, CircuitConfiguration>(CodeComparer);
        foreach (var c in circuits)
        {
            if (c == null) continue;
            if (string.IsNullOrWhiteSpace(c.Code)) continue;
            // Last one wins if duplicates exist (keeps behavior deterministic)


            // Last one wins if duplicates exist (keeps behavior deterministic)
            dict[c.Code] = c;
        }


        return dict;
    }

    /// <summary>
    /// Initializes the complete built-in circuit dataset (embedded <c>Resources/circuits.json</c>).
    /// Returns new instances on every call.
    /// </summary>
    public IList<CircuitConfiguration> InitializeCircuits()
    {
        // Generated from database 21-06-23 10:30:25
        using (var stream = typeof(StaticCircuitService).Assembly.GetManifestResourceStream(CircuitsResource))
        {
            if (stream == null)
                throw new InvalidOperationException($"Embedded resource '{CircuitsResource}' not found.");

            using (var document = JsonDocument.Parse(stream))
            {
                var tracks = new List<CircuitConfiguration>();
                foreach (var element in document.RootElement.EnumerateArray())
                    tracks.Add(ReadCircuit(element));
                return tracks;
            }
        }
    }

    private const string CircuitsResource = "LapViz.Telemetry.Resources.circuits.json";

    /// <summary>
    /// Reads one circuit of the compact embedded format: optional fields are omitted when they hold
    /// their default value, coordinates are <c>[lat, lon]</c> and segments <c>[number, start, end]</c>.
    /// </summary>
    private static CircuitConfiguration ReadCircuit(JsonElement e)
    {
        var circuit = new CircuitConfiguration
        {
            Id = OptString(e, "id"),
            Name = OptString(e, "name"),
            Location = OptString(e, "location"),
            Code = OptString(e, "code"),
            CountryCode = OptString(e, "country"),
            Type = e.TryGetProperty("type", out var type)
                ? (CircuitType)Enum.Parse(typeof(CircuitType), type.GetString() ?? nameof(CircuitType.Closed), ignoreCase: false)
                : CircuitType.Closed,
            UseDirection = e.TryGetProperty("useDirection", out var useDirection) && useDirection.GetBoolean(),
            SectorTimeout = e.TryGetProperty("sectorTimeout", out var sectorTimeout) ? sectorTimeout.GetInt32() : 0,
            Zoom = e.TryGetProperty("zoom", out var zoom) ? zoom.GetInt32() : 0,
            WeatherForecastCode = OptString(e, "weather"),
            Test = e.TryGetProperty("test", out var test) && test.GetBoolean(),
            Updated = e.TryGetProperty("updated", out var updated) ? updated.GetDateTimeOffset() : default,
            Center = e.TryGetProperty("center", out var center) ? ReadPoint(center) : null,
            BoundingBox = e.TryGetProperty("box", out var box) ? ReadLine(box[0], box[1]) : null
        };

        foreach (var segment in e.GetProperty("segments").EnumerateArray())
        {
            circuit.Segments.Add(new CircuitSegment
            {
                Number = segment[0].GetInt32(),
                Boundary = ReadLine(segment[1], segment[2])
            });
        }

        return circuit;
    }

    private static string OptString(JsonElement e, string name)
        => e.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static GeoCoordinates ReadPoint(JsonElement point)
        => new GeoCoordinates(point[0].GetDouble(), point[1].GetDouble());

    private static CircuitGeoLine ReadLine(JsonElement start, JsonElement end)
        => new CircuitGeoLine(ReadPoint(start), ReadPoint(end));
}
