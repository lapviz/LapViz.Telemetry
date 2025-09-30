using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LapViz.Telemetry.Abstractions;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Services;

/// <summary>
/// ICircuitService implementation backed by an in-memory, static list of circuits. FOR TESTING ONLY.
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
            dict[c.Code] = c;
        }
        return dict;
    }

    /// <summary>
    /// Initializes the complete built-in circuit dataset.
    /// </summary>
    public IList<CircuitConfiguration> InitializeCircuits()
    {
        IList<CircuitConfiguration> tracks = new List<CircuitConfiguration>();

        // Weather : https://weatherwidget.io/

        // Generated from database 21-06-23 10:30:25
        var czolder = new CircuitConfiguration
        {
            Name = "Zolder",
            Code = "zolder",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.997616, 5.245668), new GeoCoordinates(50.983684, 5.268093)),
            UseDirection = true,
            Center = new GeoCoordinates(50.997616, 5.245668),
            Zoom = 17,
            Test = false
        };
        czolder.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.988809, 5.255925), new GeoCoordinates(50.989281, 5.255501)) });
        tracks.Add(czolder);

        var cmettet = new CircuitConfiguration
        {
            Name = "Mettet",
            Code = "mettet",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.302476, 4.646330), new GeoCoordinates(50.299552, 4.656706)),
            UseDirection = true,
            Center = new GeoCoordinates(50.302476, 4.646330),
            Zoom = 17,
            Test = false
        };
        cmettet.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.301146, 4.651559), new GeoCoordinates(50.301537, 4.651340)) });
        cmettet.Segments.Add(new CircuitSegment() { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(50.300857, 4.650460), new GeoCoordinates(50.300616, 4.650594)) });
        cmettet.Segments.Add(new CircuitSegment() { Number = 3, Boundary = new CircuitGeoLine(new GeoCoordinates(50.300443, 4.653965), new GeoCoordinates(50.300161, 4.654117)) });
        tracks.Add(cmettet);

        var cabbeville = new CircuitConfiguration
        {
            Name = "Circuit d'Abbeville",
            Code = "abbeville",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.136697, 1.827562), new GeoCoordinates(50.133248, 1.837531)),
            UseDirection = true,
            Center = new GeoCoordinates(50.136697, 1.827562),
            Zoom = 17,
            Test = false
        };
        cabbeville.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.133865, 1.833697), new GeoCoordinates(50.134418, 1.833697)) });
        tracks.Add(cabbeville);

        var cales = new CircuitConfiguration
        {
            Name = "Circuit d'Alès",
            Code = "ales",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(44.160880, 4.070174), new GeoCoordinates(44.152798, 4.075088)),
            UseDirection = true,
            Center = new GeoCoordinates(44.160880, 4.070174),
            Zoom = 17,
            Test = false
        };
        cales.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(44.155039, 4.071185), new GeoCoordinates(44.155069, 4.071760)) });
        tracks.Add(cales);

        var canneaudurhin = new CircuitConfiguration
        {
            Name = "Anneau du rhin",
            Code = "anneaudurhin",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(47.951444, 7.415584), new GeoCoordinates(47.945267, 7.429207)),
            UseDirection = true,
            Center = new GeoCoordinates(47.951444, 7.415584),
            Zoom = 17,
            Test = false
        };
        canneaudurhin.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(47.946326, 7.426402), new GeoCoordinates(47.946570, 7.426675)) });
        tracks.Add(canneaudurhin);

        var cbresse = new CircuitConfiguration
        {
            Name = "Circuit de Bresse",
            Code = "bresse",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(46.554707, 5.324468), new GeoCoordinates(46.549654, 5.332968)),
            UseDirection = true,
            Center = new GeoCoordinates(46.554707, 5.324468),
            Zoom = 17,
            Test = false
        };
        cbresse.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(46.551499, 5.328358), new GeoCoordinates(46.551818, 5.328856)) });
        tracks.Add(cbresse);

        var ccarole = new CircuitConfiguration
        {
            Name = "Circuit Carole",
            Code = "carole",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(48.983337, 2.518387), new GeoCoordinates(48.977206, 2.524327)),
            UseDirection = true,
            Center = new GeoCoordinates(48.983337, 2.518387),
            Zoom = 17,
            Test = false
        };
        ccarole.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(48.979068, 2.522535), new GeoCoordinates(48.979112, 2.523028)) });
        tracks.Add(ccarole);

        var cchambley = new CircuitConfiguration
        {
            Name = "Circuit de Chambley",
            Code = "chambley",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(49.031345, 5.883811), new GeoCoordinates(49.024662, 5.895548)),
            UseDirection = true,
            Center = new GeoCoordinates(49.031345, 5.883811),
            Zoom = 17,
            Test = false
        };
        cchambley.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(49.026708, 5.891808), new GeoCoordinates(49.027165, 5.891912)) });
        tracks.Add(cchambley);

        var cgenk = new CircuitConfiguration
        {
            Name = "Genk",
            Code = "genk",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.989596, 5.561016), new GeoCoordinates(50.985612, 5.567775)),
            UseDirection = true,
            Center = new GeoCoordinates(50.989596, 5.561016),
            Zoom = 17,
            Test = false
        };
        cgenk.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.988481, 5.564474), new GeoCoordinates(50.988354, 5.564307)) });
        cgenk.Segments.Add(new CircuitSegment() { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(50.986618, 5.565205), new GeoCoordinates(50.986741, 5.565035)) });
        cgenk.Segments.Add(new CircuitSegment() { Number = 3, Boundary = new CircuitGeoLine(new GeoCoordinates(50.987306, 5.564108), new GeoCoordinates(50.987460, 5.564293)) });
        tracks.Add(cgenk);

        var cmariembourg = new CircuitConfiguration
        {
            Name = "Karting des Fagnes",
            Code = "mariembourg",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.095075, 4.496925), new GeoCoordinates(50.092157, 4.503523)),
            UseDirection = true,
            Center = new GeoCoordinates(50.095075, 4.496925),
            Zoom = 17,
            Test = false
        };
        cmariembourg.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.093955, 4.501539), new GeoCoordinates(50.093777, 4.501556)) });
        cmariembourg.Segments.Add(new CircuitSegment() { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(50.093392, 4.501248), new GeoCoordinates(50.093193, 4.501257)) });
        cmariembourg.Segments.Add(new CircuitSegment() { Number = 3, Boundary = new CircuitGeoLine(new GeoCoordinates(50.093296, 4.499475), new GeoCoordinates(50.093488, 4.499471)) });
        tracks.Add(cmariembourg);

        var cspakarting = new CircuitConfiguration
        {
            Name = "Karting Spa-Francorchamps",
            Code = "spakarting",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.433561, 5.960235), new GeoCoordinates(50.430916, 5.965202)),
            UseDirection = true,
            Center = new GeoCoordinates(50.433561, 5.960235),
            Zoom = 17,
            Test = false
        };
        cspakarting.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.432834, 5.962789), new GeoCoordinates(50.432706, 5.962945)) });
        cspakarting.Segments.Add(new CircuitSegment() { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(50.431857, 5.962458), new GeoCoordinates(50.431748, 5.962338)) });
        cspakarting.Segments.Add(new CircuitSegment() { Number = 3, Boundary = new CircuitGeoLine(new GeoCoordinates(50.432308, 5.963896), new GeoCoordinates(50.432380, 5.963708)) });
        tracks.Add(cspakarting);

        var costricourt = new CircuitConfiguration
        {
            Name = "Karting d'Ostricourt",
            Code = "ostricourt",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.470406, 3.027950), new GeoCoordinates(50.466869, 3.035353)),
            UseDirection = true,
            Center = new GeoCoordinates(50.470406, 3.027950),
            Zoom = 17,
            Test = false
        };
        costricourt.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.468348, 3.030380), new GeoCoordinates(50.468467, 3.030600)) });
        costricourt.Segments.Add(new CircuitSegment() { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(50.469324, 3.031773), new GeoCoordinates(50.469481, 3.032011)) });
        costricourt.Segments.Add(new CircuitSegment() { Number = 3, Boundary = new CircuitGeoLine(new GeoCoordinates(50.469072, 3.032200), new GeoCoordinates(50.468924, 3.032011)) });
        tracks.Add(costricourt);

        var cnandrintest2 = new CircuitConfiguration
        {
            Name = "Nandrin Test Croix Claire",
            Code = "nandrintest2",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.514957, 5.421977), new GeoCoordinates(50.512115, 5.427701)),
            UseDirection = true,
            Center = new GeoCoordinates(50.514957, 5.421977),
            Zoom = 17,
            Test = true
        };
        cnandrintest2.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.514361, 5.425949), new GeoCoordinates(50.514583, 5.426357)) });
        cnandrintest2.Segments.Add(new CircuitSegment() { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(50.513631, 5.423674), new GeoCoordinates(50.513420, 5.423004)) });
        cnandrintest2.Segments.Add(new CircuitSegment() { Number = 3, Boundary = new CircuitGeoLine(new GeoCoordinates(50.513396, 5.425235), new GeoCoordinates(50.513123, 5.425632)) });
        tracks.Add(cnandrintest2);

        var cnandrintest4 = new CircuitConfiguration
        {
            Name = "Nandrin Test Croix Claire 2",
            Code = "nandrintest4",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.517870, 5.422800), new GeoCoordinates(50.515274, 5.427628)),
            UseDirection = true,
            Center = new GeoCoordinates(50.517870, 5.422800),
            Zoom = 17,
            Test = true
        };
        cnandrintest4.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.516612, 5.426550), new GeoCoordinates(50.516646, 5.427295)) });
        cnandrintest4.Segments.Add(new CircuitSegment() { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(50.516855, 5.424554), new GeoCoordinates(50.517309, 5.424227)) });
        cnandrintest4.Segments.Add(new CircuitSegment() { Number = 3, Boundary = new CircuitGeoLine(new GeoCoordinates(50.516069, 5.424973), new GeoCoordinates(50.515789, 5.425632)) });
        tracks.Add(cnandrintest4);

        var ceindhoven = new CircuitConfiguration
        {
            Name = "Karting Eindhoven",
            Code = "eindhoven",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.463313, 5.372876), new GeoCoordinates(51.459950, 5.376907)),
            UseDirection = true,
            Center = new GeoCoordinates(51.463313, 5.372876),
            Zoom = 17,
            Test = false
        };
        ceindhoven.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.462112, 5.375552), new GeoCoordinates(51.461906, 5.375466)) });
        ceindhoven.Segments.Add(new CircuitSegment() { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(51.461363, 5.375721), new GeoCoordinates(51.461460, 5.375402)) });
        ceindhoven.Segments.Add(new CircuitSegment() { Number = 3, Boundary = new CircuitGeoLine(new GeoCoordinates(51.462332, 5.374560), new GeoCoordinates(51.462330, 5.374329)) });
        tracks.Add(ceindhoven);

        var cadria = new CircuitConfiguration
        {
            Name = "Karting Adria",
            Code = "adria",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(45.047297, 12.149337), new GeoCoordinates(45.044483, 12.152057)),
            UseDirection = true,
            Center = new GeoCoordinates(45.047297, 12.149337),
            Zoom = 17,
            Test = false
        };
        cadria.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(45.046104, 12.149652), new GeoCoordinates(45.046047, 12.150003)) });
        cadria.Segments.Add(new CircuitSegment() { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(45.045767, 12.150278), new GeoCoordinates(45.045848, 12.149991)) });
        cadria.Segments.Add(new CircuitSegment() { Number = 3, Boundary = new CircuitGeoLine(new GeoCoordinates(45.045429, 12.150957), new GeoCoordinates(45.045504, 12.150670)) });
        tracks.Add(cadria);

        var czuera = new CircuitConfiguration
        {
            Name = "Karting Zuera",
            Code = "zuera",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(41.828629, -0.815664), new GeoCoordinates(41.825095, -0.810128)),
            UseDirection = true,
            Center = new GeoCoordinates(41.828629, -0.815664),
            Zoom = 17,
            Test = false
        };
        czuera.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(41.828041, -0.811994), new GeoCoordinates(41.827789, -0.812145)) });
        czuera.Segments.Add(new CircuitSegment() { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(41.826746, -0.812327), new GeoCoordinates(41.826430, -0.812198)) });
        czuera.Segments.Add(new CircuitSegment() { Number = 3, Boundary = new CircuitGeoLine(new GeoCoordinates(41.826578, -0.813019), new GeoCoordinates(41.826866, -0.813148)) });
        tracks.Add(czuera);

        var clemanskarting = new CircuitConfiguration
        {
            Name = "Karting Le Mans",
            Code = "lemanskarting",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(47.944045, 0.210353), new GeoCoordinates(47.940892, 0.213963)),
            UseDirection = true,
            Center = new GeoCoordinates(47.944045, 0.210353),
            Zoom = 17,
            Test = false
        };
        clemanskarting.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(47.942653, 0.211538), new GeoCoordinates(47.942642, 0.211189)) });
        clemanskarting.Segments.Add(new CircuitSegment() { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(47.942319, 0.211640), new GeoCoordinates(47.942333, 0.211930)) });
        clemanskarting.Segments.Add(new CircuitSegment() { Number = 3, Boundary = new CircuitGeoLine(new GeoCoordinates(47.942128, 0.212729), new GeoCoordinates(47.942103, 0.212418)) });
        clemanskarting.Segments.Add(new CircuitSegment() { Number = 4, Boundary = new CircuitGeoLine(new GeoCoordinates(47.942703, 0.213216), new GeoCoordinates(47.942707, 0.213500)) });
        tracks.Add(clemanskarting);

        var criversaltes = new CircuitConfiguration
        {
            Name = "Karting Rivesaltes",
            Code = "riversaltes",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(42.781276, 2.904665), new GeoCoordinates(42.778071, 2.910963)),
            UseDirection = true,
            Center = new GeoCoordinates(42.781276, 2.904665),
            Zoom = 17,
            Test = false
        };
        criversaltes.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(42.779800, 2.909087), new GeoCoordinates(42.779627, 2.909028)) });
        criversaltes.Segments.Add(new CircuitSegment() { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(42.779316, 2.909045), new GeoCoordinates(42.779088, 2.908972)) });
        criversaltes.Segments.Add(new CircuitSegment() { Number = 3, Boundary = new CircuitGeoLine(new GeoCoordinates(42.779418, 2.907343), new GeoCoordinates(42.779491, 2.907040)) });
        criversaltes.Segments.Add(new CircuitSegment() { Number = 4, Boundary = new CircuitGeoLine(new GeoCoordinates(42.779830, 2.905672), new GeoCoordinates(42.779954, 2.905948)) });
        tracks.Add(criversaltes);

        var ccharage = new CircuitConfiguration
        {
            Name = "Circuit de charade",
            Code = "charage",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(45.745115, 3.024700), new GeoCoordinates(45.738131, 3.042573)),
            UseDirection = true,
            Center = new GeoCoordinates(45.745115, 3.024700),
            Zoom = 17,
            Test = false
        };
        ccharage.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(45.741168, 3.027598), new GeoCoordinates(45.740846, 3.027845)) });
        tracks.Add(ccharage);

        var clurcylevis = new CircuitConfiguration
        {
            Name = "Circuit lurcy levis",
            Code = "lurcylevis",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(46.719368, 2.935740), new GeoCoordinates(46.710409, 2.959472)),
            UseDirection = true,
            Center = new GeoCoordinates(46.719368, 2.935740),
            Zoom = 17,
            Test = false
        };
        clurcylevis.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(46.717326, 2.947547), new GeoCoordinates(46.717076, 2.947938)) });
        tracks.Add(clurcylevis);

        var cpaulamagnac = new CircuitConfiguration
        {
            Name = "Circuit paul amagnac",
            Code = "paulamagnac",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(43.774412, -0.046997), new GeoCoordinates(43.765607, -0.034562)),
            UseDirection = true,
            Center = new GeoCoordinates(43.774412, -0.046997),
            Zoom = 17,
            Test = false
        };
        cpaulamagnac.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(43.770539, -0.040836), new GeoCoordinates(43.770754, -0.040308)) });
        tracks.Add(cpaulamagnac);

        var cpaularnos = new CircuitConfiguration
        {
            Name = "Circuit paul Arnos",
            Code = "paularnos",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(43.449821, -0.539646), new GeoCoordinates(43.442094, -0.530169)),
            UseDirection = true,
            Center = new GeoCoordinates(43.449821, -0.539646),
            Zoom = 17,
            Test = false
        };
        cpaularnos.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(43.447148, -0.532407), new GeoCoordinates(43.447035, -0.532779)) });
        tracks.Add(cpaularnos);

        var cpaulricard = new CircuitConfiguration
        {
            Name = "Circuit paul ricard",
            Code = "paulricard",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(43.259635, 5.777508), new GeoCoordinates(43.244620, 5.805720)),
            UseDirection = true,
            Center = new GeoCoordinates(43.259635, 5.777508),
            Zoom = 17,
            Test = false
        };
        cpaulricard.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(43.252494, 5.790279), new GeoCoordinates(43.252869, 5.790688)) });
        tracks.Add(cpaulricard);

        var ccroixenternois = new CircuitConfiguration
        {
            Name = "Circuit croix en ternois",
            Code = "croixenternois",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.380682, 2.292009), new GeoCoordinates(50.376715, 2.301706)),
            UseDirection = true,
            Center = new GeoCoordinates(50.380682, 2.292009),
            Zoom = 17,
            Test = false
        };
        ccroixenternois.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.379096, 2.296175), new GeoCoordinates(50.378806, 2.296370)) });
        tracks.Add(ccroixenternois);

        var c100 = new CircuitConfiguration
        {
            Name = "Circuit dijon pernois",
            Code = "100",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(47.368754, 4.888694), new GeoCoordinates(47.356538, 4.906304)),
            UseDirection = true,
            Center = new GeoCoordinates(47.368754, 4.888694),
            Zoom = 17,
            Test = false
        };
        c100.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(47.364962, 4.899589), new GeoCoordinates(47.364670, 4.900068)) });
        tracks.Add(c100);

        var c101 = new CircuitConfiguration
        {
            Name = "Circuit du var",
            Code = "101",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(43.350597, 6.331349), new GeoCoordinates(43.343893, 6.340830)),
            UseDirection = true,
            Center = new GeoCoordinates(43.350597, 6.331349),
            Zoom = 17,
            Test = false
        };
        c101.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(43.348383, 6.333200), new GeoCoordinates(43.348256, 6.333884)) });
        tracks.Add(c101);

        var c102 = new CircuitConfiguration
        {
            Name = "Circuit des ecuyers",
            Code = "102",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(49.111948, 3.506584), new GeoCoordinates(49.105529, 3.514545)),
            UseDirection = true,
            Center = new GeoCoordinates(49.111948, 3.506584),
            Zoom = 17,
            Test = false
        };
        c102.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(49.108411, 3.508020), new GeoCoordinates(49.108348, 3.508591)) });
        tracks.Add(c102);

        var c103 = new CircuitConfiguration
        {
            Name = "Circuit de folembray",
            Code = "103",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(49.549691, 3.297246), new GeoCoordinates(49.542117, 3.309627)),
            UseDirection = true,
            Center = new GeoCoordinates(49.549691, 3.297246),
            Zoom = 17,
            Test = false
        };
        c103.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(49.544490, 3.301056), new GeoCoordinates(49.544280, 3.301360)) });
        tracks.Add(c103);

        var c104 = new CircuitConfiguration
        {
            Name = "Circuit de vendée",
            Code = "104",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(46.439845, -0.801163), new GeoCoordinates(46.436511, -0.789018)),
            UseDirection = true,
            Center = new GeoCoordinates(46.439845, -0.801163),
            Zoom = 17,
            Test = false
        };
        c104.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(46.438391, -0.790990), new GeoCoordinates(46.438713, -0.791381)) });
        tracks.Add(c104);

        var c105 = new CircuitConfiguration
        {
            Name = "Circuit de haute saintonge",
            Code = "105",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(45.243327, -0.097123), new GeoCoordinates(45.238559, -0.087502)),
            UseDirection = true,
            Center = new GeoCoordinates(45.243327, -0.097123),
            Zoom = 17,
            Test = false
        };
        c105.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(45.240429, -0.094536), new GeoCoordinates(45.240741, -0.094286)) });
        tracks.Add(c105);

        var c106 = new CircuitConfiguration
        {
            Name = "Circuit de la charte",
            Code = "106",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(46.595315, 2.003959), new GeoCoordinates(46.591507, 2.008050)),
            UseDirection = true,
            Center = new GeoCoordinates(46.595315, 2.003959),
            Zoom = 17,
            Test = false
        };
        c106.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(46.592898, 2.006552), new GeoCoordinates(46.592687, 2.006876)) });
        tracks.Add(c106);

        var c107 = new CircuitConfiguration
        {
            Name = "Circuit de lédenon",
            Code = "107",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(43.928535, 4.499943), new GeoCoordinates(43.919683, 4.511553)),
            UseDirection = true,
            Center = new GeoCoordinates(43.928535, 4.499943),
            Zoom = 17,
            Test = false
        };
        c107.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(43.923141, 4.504438), new GeoCoordinates(43.923233, 4.503802)) });
        tracks.Add(c107);

        var c108 = new CircuitConfiguration
        {
            Name = "Le Mans",
            Code = "108",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(47.959500, 0.206361), new GeoCoordinates(47.947371, 0.220976)),
            UseDirection = true,
            Center = new GeoCoordinates(47.959500, 0.206361),
            Zoom = 17,
            Test = false
        };
        c108.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(47.951185, 0.207135), new GeoCoordinates(47.951170, 0.208042)) });
        tracks.Add(c108);

        var c109 = new CircuitConfiguration
        {
            Name = "Le mans historique",
            Code = "109",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(47.962710, 0.202897), new GeoCoordinates(47.911832, 0.247493)),
            UseDirection = true,
            Center = new GeoCoordinates(47.962710, 0.202897),
            Zoom = 17,
            Test = false
        };
        c109.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(47.951185, 0.207136), new GeoCoordinates(47.951170, 0.208043)) });
        tracks.Add(c109);

        var c110 = new CircuitConfiguration
        {
            Name = "Circuit de la ferte gaucher-grand",
            Code = "110",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(48.760974, 3.275960), new GeoCoordinates(48.754301, 3.288443)),
            UseDirection = true,
            Center = new GeoCoordinates(48.760974, 3.275960),
            Zoom = 17,
            Test = false
        };
        c110.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(48.757055, 3.279835), new GeoCoordinates(48.757432, 3.279239)) });
        tracks.Add(c110);

        var c111 = new CircuitConfiguration
        {
            Name = "Circuit des clubs",
            Code = "111",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(46.871030, 3.149405), new GeoCoordinates(46.862592, 3.158888)),
            UseDirection = true,
            Center = new GeoCoordinates(46.871030, 3.149405),
            Zoom = 17,
            Test = false
        };
        c111.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(46.866604, 3.156404), new GeoCoordinates(46.866897, 3.156334)) });
        tracks.Add(c111);

        var c112 = new CircuitConfiguration
        {
            Name = "Ciste gp",
            Code = "112",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(46.869177, 3.159386), new GeoCoordinates(46.858377, 3.170380)),
            UseDirection = true,
            Center = new GeoCoordinates(46.869177, 3.159386),
            Zoom = 17,
            Test = false
        };
        c112.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(46.864207, 3.163552), new GeoCoordinates(46.863912, 3.163901)) });
        tracks.Add(c112);

        var c113 = new CircuitConfiguration
        {
            Name = "Circuit de kart",
            Code = "113",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(46.863703, 3.153725), new GeoCoordinates(46.861588, 3.157738)),
            UseDirection = true,
            Center = new GeoCoordinates(46.863703, 3.153725),
            Zoom = 17,
            Test = false
        };
        c113.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(46.862389, 3.156859), new GeoCoordinates(46.862555, 3.156605)) });
        tracks.Add(c113);

        var c114 = new CircuitConfiguration
        {
            Name = "Circuit du grand sambuc",
            Code = "114",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(43.582716, 5.595481), new GeoCoordinates(43.578830, 5.606964)),
            UseDirection = true,
            Center = new GeoCoordinates(43.582716, 5.595481),
            Zoom = 17,
            Test = false
        };
        c114.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(43.580864, 5.599282), new GeoCoordinates(43.580511, 5.599581)) });
        tracks.Add(c114);

        var c115 = new CircuitConfiguration
        {
            Name = "Piste de vaison",
            Code = "115",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(46.766641, 4.438871), new GeoCoordinates(46.762268, 4.443919)),
            UseDirection = true,
            Center = new GeoCoordinates(46.766641, 4.438871),
            Zoom = 17,
            Test = false
        };
        c115.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(46.764963, 4.441946), new GeoCoordinates(46.764821, 4.441501)) });
        tracks.Add(c115);

        var c116 = new CircuitConfiguration
        {
            Name = "Circuit du val de vienne",
            Code = "116",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(46.198436, 0.622301), new GeoCoordinates(46.192578, 0.640742)),
            UseDirection = true,
            Center = new GeoCoordinates(46.198436, 0.622301),
            Zoom = 17,
            Test = false
        };
        c116.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(46.197470, 0.635927), new GeoCoordinates(46.196992, 0.635748)) });
        tracks.Add(c116);

        var c117 = new CircuitConfiguration
        {
            Name = "TT circuit assen",
            Code = "117",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.965702, 6.506659), new GeoCoordinates(52.950665, 6.534370)),
            UseDirection = true,
            Center = new GeoCoordinates(52.965702, 6.506659),
            Zoom = 17,
            Test = false
        };
        c117.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.962531, 6.523819), new GeoCoordinates(52.962108, 6.524381)) });
        tracks.Add(c117);

        var c118 = new CircuitConfiguration
        {
            Name = "Circuit park zandvoort",
            Code = "118",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.392770, 4.536965), new GeoCoordinates(52.383294, 4.553946)),
            UseDirection = true,
            Center = new GeoCoordinates(52.392770, 4.536965),
            Zoom = 17,
            Test = false
        };
        c118.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.389560, 4.540707), new GeoCoordinates(52.389370, 4.541643)) });
        tracks.Add(c118);

        var c119 = new CircuitConfiguration
        {
            Name = "Bilster Berg",
            Code = "119",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.797217, 9.055524), new GeoCoordinates(51.788038, 9.077345)),
            UseDirection = true,
            Center = new GeoCoordinates(51.797217, 9.055524),
            Zoom = 17,
            Test = false
        };
        c119.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.793753, 9.071602), new GeoCoordinates(51.793493, 9.070970)) });
        tracks.Add(c119);

        var c120 = new CircuitConfiguration
        {
            Name = "Hockenheim-Ring",
            Code = "120",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(49.335505, 8.562011), new GeoCoordinates(49.335505, 8.562011)),
            UseDirection = true,
            Center = new GeoCoordinates(49.335505, 8.562011),
            Zoom = 17,
            Test = false
        };
        c120.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(49.327688, 8.565560), new GeoCoordinates(49.327881, 8.566210)) });
        tracks.Add(c120);

        var c121 = new CircuitConfiguration
        {
            Name = "EuroSpeedway Lausitz-Grand Pix",
            Code = "121",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.536974, 13.919376), new GeoCoordinates(51.526593, 13.937099)),
            UseDirection = true,
            Center = new GeoCoordinates(51.536974, 13.919376),
            Zoom = 17,
            Test = false
        };
        c121.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.534746, 13.928399), new GeoCoordinates(51.535441, 13.928091)) });
        tracks.Add(c121);

        var c122 = new CircuitConfiguration
        {
            Name = "Motorsport Arena Oschersleben",
            Code = "122",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.031469, 11.269004), new GeoCoordinates(52.025115, 11.285578)),
            UseDirection = true,
            Center = new GeoCoordinates(52.031469, 11.269004),
            Zoom = 17,
            Test = false
        };
        c122.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.026811, 11.280220), new GeoCoordinates(52.027376, 11.280453)) });
        tracks.Add(c122);

        var c123 = new CircuitConfiguration
        {
            Name = "Nurburgring-Grand Pix",
            Code = "123",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.338771, 6.929817), new GeoCoordinates(50.322504, 6.952185)),
            UseDirection = true,
            Center = new GeoCoordinates(50.338771, 6.929817),
            Zoom = 17,
            Test = false
        };
        c123.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.335367, 6.947888), new GeoCoordinates(50.335802, 6.947222)) });
        tracks.Add(c123);

        var c124 = new CircuitConfiguration
        {
            Name = "Nurburgring-Nordschleife",
            Code = "124",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.384933, 6.916486), new GeoCoordinates(50.327409, 7.014833)),
            UseDirection = true,
            Center = new GeoCoordinates(50.384933, 6.916486),
            Zoom = 17,
            Test = false
        };
        c124.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.344073, 6.962139), new GeoCoordinates(50.344597, 6.961650)) });
        tracks.Add(c124);

        var c125 = new CircuitConfiguration
        {
            Name = "Porsche Leipzig on-road Circuit",
            Code = "125",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.411024, 12.295013), new GeoCoordinates(51.399432, 12.303748)),
            UseDirection = true,
            Center = new GeoCoordinates(51.411024, 12.295013),
            Zoom = 17,
            Test = false
        };
        c125.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.405494, 12.297131), new GeoCoordinates(51.405561, 12.297887)) });
        tracks.Add(c125);

        var c126 = new CircuitConfiguration
        {
            Name = "Sachsenring Circuit",
            Code = "126",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.794224, 12.680779), new GeoCoordinates(50.787938, 12.696533)),
            UseDirection = true,
            Center = new GeoCoordinates(50.794224, 12.680779),
            Zoom = 17,
            Test = false
        };
        c126.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.791964, 12.687848), new GeoCoordinates(50.791586, 12.688240)) });
        tracks.Add(c126);

        var c127 = new CircuitConfiguration
        {
            Name = "Red Bull Ring",
            Code = "127",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(47.227146, 14.752643), new GeoCoordinates(47.218082, 14.772255)),
            UseDirection = true,
            Center = new GeoCoordinates(47.227146, 14.752643),
            Zoom = 17,
            Test = false
        };
        c127.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(47.220080, 14.766825), new GeoCoordinates(47.220545, 14.766653)) });
        tracks.Add(c127);

        var c128 = new CircuitConfiguration
        {
            Name = "Salzburgring",
            Code = "128",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(47.824632, 13.151953), new GeoCoordinates(47.819627, 13.178558)),
            UseDirection = true,
            Center = new GeoCoordinates(47.824632, 13.151953),
            Zoom = 17,
            Test = false
        };
        c128.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(47.822768, 13.169153), new GeoCoordinates(47.823343, 13.168974)) });
        tracks.Add(c128);

        var c129 = new CircuitConfiguration
        {
            Name = "Wachauring Melk",
            Code = "129",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(48.214314, 15.324148), new GeoCoordinates(48.212024, 15.330675)),
            UseDirection = true,
            Center = new GeoCoordinates(48.214314, 15.324148),
            Zoom = 17,
            Test = false
        };
        c129.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(48.212481, 15.327669), new GeoCoordinates(48.212952, 15.327499)) });
        tracks.Add(c129);

        var c130 = new CircuitConfiguration
        {
            Name = "Zaluzani",
            Code = "130",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(44.855858, 17.221893), new GeoCoordinates(44.844082, 17.232052)),
            UseDirection = true,
            Center = new GeoCoordinates(44.855858, 17.221893),
            Zoom = 17,
            Test = false
        };
        c130.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(44.853406, 17.226943), new GeoCoordinates(44.853305, 17.226600)) });
        tracks.Add(c130);

        var c131 = new CircuitConfiguration
        {
            Name = "Dragon Race Track",
            Code = "131",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(42.343811, 24.731170), new GeoCoordinates(42.338837, 24.738813)),
            UseDirection = true,
            Center = new GeoCoordinates(42.343811, 24.731170),
            Zoom = 17,
            Test = false
        };
        c131.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(42.340879, 24.734110), new GeoCoordinates(42.341188, 24.734593)) });
        tracks.Add(c131);

        var c132 = new CircuitConfiguration
        {
            Name = "Automotodrom Grobnik",
            Code = "132",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(45.385597, 14.497569), new GeoCoordinates(45.379104, 14.520991)),
            UseDirection = true,
            Center = new GeoCoordinates(45.385597, 14.497569),
            Zoom = 17,
            Test = false
        };
        c132.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(45.382534, 14.509388), new GeoCoordinates(45.382988, 14.509365)) });
        tracks.Add(c132);

        var c133 = new CircuitConfiguration
        {
            Name = "Raus Novi Marof",
            Code = "133",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(46.157318, 16.347504), new GeoCoordinates(46.154703, 16.350745)),
            UseDirection = true,
            Center = new GeoCoordinates(46.157318, 16.347504),
            Zoom = 17,
            Test = false
        };
        c133.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(46.155238, 16.348348), new GeoCoordinates(46.154858, 16.348267)) });
        tracks.Add(c133);

        var c134 = new CircuitConfiguration
        {
            Name = "Achna Speedway",
            Code = "134",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(35.027838, 33.797376), new GeoCoordinates(35.023067, 33.802857)),
            UseDirection = true,
            Center = new GeoCoordinates(35.027838, 33.797376),
            Zoom = 17,
            Test = false
        };
        c134.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(35.026702, 33.799596), new GeoCoordinates(35.026387, 33.799466)) });
        tracks.Add(c134);

        var c135 = new CircuitConfiguration
        {
            Name = "Brno Circuit",
            Code = "135",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(49.211075, 16.437804), new GeoCoordinates(49.200026, 16.465818)),
            UseDirection = true,
            Center = new GeoCoordinates(49.211075, 16.437804),
            Zoom = 17,
            Test = false
        };
        c135.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(49.202611, 16.445309), new GeoCoordinates(49.203093, 16.445606)) });
        tracks.Add(c135);

        var c136 = new CircuitConfiguration
        {
            Name = "Autodrom Most",
            Code = "136",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.522404, 13.588392), new GeoCoordinates(50.514580, 13.614784)),
            UseDirection = true,
            Center = new GeoCoordinates(50.522404, 13.588392),
            Zoom = 17,
            Test = false
        };
        c136.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.519249, 13.607708), new GeoCoordinates(50.519689, 13.607808)) });
        tracks.Add(c136);

        var c137 = new CircuitConfiguration
        {
            Name = "Autodrome Pisek",
            Code = "137",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(49.304768, 14.112797), new GeoCoordinates(49.300988, 14.116466)),
            UseDirection = true,
            Center = new GeoCoordinates(49.304768, 14.112797),
            Zoom = 17,
            Test = false
        };
        c137.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(49.302106, 14.114525), new GeoCoordinates(49.301981, 14.115205)) });
        tracks.Add(c137);

        var c138 = new CircuitConfiguration
        {
            Name = "Circuit de Sosnova",
            Code = "138",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.661219, 14.535515), new GeoCoordinates(50.657967, 14.542319)),
            UseDirection = true,
            Center = new GeoCoordinates(50.661219, 14.535515),
            Zoom = 17,
            Test = false
        };
        c138.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.658718, 14.540104), new GeoCoordinates(50.658447, 14.540063)) });
        tracks.Add(c138);

        var c139 = new CircuitConfiguration
        {
            Name = "Autodrom Vysoke Myto",
            Code = "139",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(49.940995, 16.167539), new GeoCoordinates(49.937997, 16.172956)),
            UseDirection = true,
            Center = new GeoCoordinates(49.940995, 16.167539),
            Zoom = 17,
            Test = false
        };
        c139.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(49.938889, 16.168706), new GeoCoordinates(49.939004, 16.169008)) });
        tracks.Add(c139);

        var c140 = new CircuitConfiguration
        {
            Name = "Ring Djursland",
            Code = "140",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(56.338329, 10.679393), new GeoCoordinates(56.336055, 10.689196)),
            UseDirection = true,
            Center = new GeoCoordinates(56.338329, 10.679393),
            Zoom = 17,
            Test = false
        };
        c140.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(56.337417, 10.682447), new GeoCoordinates(56.337074, 10.682400)) });
        tracks.Add(c140);

        var c141 = new CircuitConfiguration
        {
            Name = "FDM Jyllandsringen",
            Code = "141",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(56.177457, 9.655908), new GeoCoordinates(56.173804, 9.665023)),
            UseDirection = true,
            Center = new GeoCoordinates(56.177457, 9.655908),
            Zoom = 17,
            Test = false
        };
        c141.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(56.176137, 9.660376), new GeoCoordinates(56.176509, 9.660673)) });
        tracks.Add(c141);

        var c142 = new CircuitConfiguration
        {
            Name = "Padborg Park",
            Code = "142",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(54.871691, 9.269491), new GeoCoordinates(54.865042, 9.279670)),
            UseDirection = true,
            Center = new GeoCoordinates(54.871691, 9.269491),
            Zoom = 17,
            Test = false
        };
        c142.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(54.869210, 9.275575), new GeoCoordinates(54.869025, 9.275867)) });
        tracks.Add(c142);

        var c143 = new CircuitConfiguration
        {
            Name = "Ahvenisto Race Circuit",
            Code = "143",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(61.004406, 24.408094), new GeoCoordinates(60.999098, 24.422253)),
            UseDirection = true,
            Center = new GeoCoordinates(61.004406, 24.408094),
            Zoom = 17,
            Test = false
        };
        c143.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(61.003706, 24.418315), new GeoCoordinates(61.003497, 24.417724)) });
        tracks.Add(c143);

        var c144 = new CircuitConfiguration
        {
            Name = "Circuit d'Alastaro",
            Code = "144",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(60.979520, 22.656438), new GeoCoordinates(60.973651, 22.672694)),
            UseDirection = true,
            Center = new GeoCoordinates(60.979520, 22.656438),
            Zoom = 17,
            Test = false
        };
        c144.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(60.976206, 22.657905), new GeoCoordinates(60.976242, 22.658591)) });
        tracks.Add(c144);

        var c145 = new CircuitConfiguration
        {
            Name = "Circuit automobile de Botniaring",
            Code = "145",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(62.626990, 22.013488), new GeoCoordinates(62.623340, 22.027629)),
            UseDirection = true,
            Center = new GeoCoordinates(62.626990, 22.013488),
            Zoom = 17,
            Test = false
        };
        c145.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(62.624429, 22.014874), new GeoCoordinates(62.624531, 22.015892)) });
        tracks.Add(c145);

        var c146 = new CircuitConfiguration
        {
            Name = "Helsingin Kartingrata",
            Code = "146",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(60.256236, 25.048949), new GeoCoordinates(60.254524, 25.052922)),
            UseDirection = true,
            Center = new GeoCoordinates(60.256236, 25.048949),
            Zoom = 17,
            Test = false
        };
        c146.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(60.256226, 25.051513), new GeoCoordinates(60.256042, 25.051559)) });
        tracks.Add(c146);

        var c147 = new CircuitConfiguration
        {
            Name = "Hyvinkaa Circuit",
            Code = "147",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(60.663066, 24.892416), new GeoCoordinates(60.660810, 24.897383)),
            UseDirection = true,
            Center = new GeoCoordinates(60.663066, 24.892416),
            Zoom = 17,
            Test = false
        };
        c147.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(60.662059, 24.894619), new GeoCoordinates(60.662052, 24.894985)) });
        tracks.Add(c147);

        var c148 = new CircuitConfiguration
        {
            Name = "Lavinto Karting",
            Code = "148",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(60.813273, 24.888375), new GeoCoordinates(60.810999, 24.893048)),
            UseDirection = true,
            Center = new GeoCoordinates(60.813273, 24.888375),
            Zoom = 17,
            Test = false
        };
        c148.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(60.812607, 24.890774), new GeoCoordinates(60.812483, 24.891003)) });
        tracks.Add(c148);

        var c149 = new CircuitConfiguration
        {
            Name = "Motopark Raceway",
            Code = "149",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(62.062891, 27.543803), new GeoCoordinates(62.054724, 27.566201)),
            UseDirection = true,
            Center = new GeoCoordinates(62.062891, 27.543803),
            Zoom = 17,
            Test = false
        };
        c149.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(62.058055, 27.559387), new GeoCoordinates(62.057787, 27.558887)) });
        tracks.Add(c149);

        var c150 = new CircuitConfiguration
        {
            Name = "Karting à Turku",
            Code = "150",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(60.484171, 22.329145), new GeoCoordinates(60.482408, 22.332429)),
            UseDirection = true,
            Center = new GeoCoordinates(60.484171, 22.329145),
            Zoom = 17,
            Test = false
        };
        c150.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(60.482888, 22.330526), new GeoCoordinates(60.483048, 22.330724)) });
        tracks.Add(c150);

        var c151 = new CircuitConfiguration
        {
            Name = "Ylamylly Karting",
            Code = "151",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(62.651677, 29.589939), new GeoCoordinates(62.649167, 29.596288)),
            UseDirection = true,
            Center = new GeoCoordinates(62.651677, 29.589939),
            Zoom = 17,
            Test = false
        };
        c151.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(62.649778, 29.592630), new GeoCoordinates(62.649952, 29.592298)) });
        tracks.Add(c151);

        var c152 = new CircuitConfiguration
        {
            Name = "Kartodromo Afidnes",
            Code = "152",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(38.179262, 23.855245), new GeoCoordinates(38.177489, 23.858476)),
            UseDirection = true,
            Center = new GeoCoordinates(38.179262, 23.855245),
            Zoom = 17,
            Test = false
        };
        c152.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(38.177739, 23.856299), new GeoCoordinates(38.177948, 23.856327)) });
        tracks.Add(c152);

        var c153 = new CircuitConfiguration
        {
            Name = "Circuit Néo Rysio",
            Code = "153",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(40.515564, 22.991752), new GeoCoordinates(40.513876, 22.995552)),
            UseDirection = true,
            Center = new GeoCoordinates(40.515564, 22.991752),
            Zoom = 17,
            Test = false
        };
        c153.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(40.514206, 22.992983), new GeoCoordinates(40.514437, 22.993030)) });
        tracks.Add(c153);

        var c154 = new CircuitConfiguration
        {
            Name = "Circuit automobile de Serres",
            Code = "154",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(41.075480, 23.510011), new GeoCoordinates(41.068748, 23.522382)),
            UseDirection = true,
            Center = new GeoCoordinates(41.075480, 23.510011),
            Zoom = 17,
            Test = false
        };
        c154.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(41.073305, 23.517992), new GeoCoordinates(41.072978, 23.517627)) });
        tracks.Add(c154);

        var c155 = new CircuitConfiguration
        {
            Name = "Anneau Euro",
            Code = "155",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(47.152321, 19.455692), new GeoCoordinates(47.143619, 19.461131)),
            UseDirection = true,
            Center = new GeoCoordinates(47.152321, 19.455692),
            Zoom = 17,
            Test = false
        };
        c155.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(47.146429, 19.458737), new GeoCoordinates(47.146402, 19.459168)) });
        tracks.Add(c155);

        var c156 = new CircuitConfiguration
        {
            Name = "Kakucs-Ring-Anti",
            Code = "156",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(47.248058, 19.376785), new GeoCoordinates(47.244505, 19.382130)),
            UseDirection = true,
            Center = new GeoCoordinates(47.248058, 19.376785),
            Zoom = 17,
            Test = false
        };
        c156.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(47.246412, 19.378150), new GeoCoordinates(47.246240, 19.377865)) });
        tracks.Add(c156);

        var c157 = new CircuitConfiguration
        {
            Name = "Mondello Park-Short Circuit",
            Code = "157",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(53.260876, -6.754061), new GeoCoordinates(53.254572, -6.742533)),
            UseDirection = true,
            Center = new GeoCoordinates(53.260876, -6.754061),
            Zoom = 17,
            Test = false
        };
        c157.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(53.257182, -6.744996), new GeoCoordinates(53.257101, -6.745384)) });
        tracks.Add(c157);

        var c158 = new CircuitConfiguration
        {
            Name = "Parc Mondello-Circuit national",
            Code = "158",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(53.260876, -6.754061), new GeoCoordinates(53.254572, -6.742533)),
            UseDirection = true,
            Center = new GeoCoordinates(53.260876, -6.754061),
            Zoom = 17,
            Test = false
        };
        c158.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(53.257182, -6.744996), new GeoCoordinates(53.257101, -6.745384)) });
        tracks.Add(c158);

        var c159 = new CircuitConfiguration
        {
            Name = "Parc Mondello",
            Code = "159",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(53.260876, -6.754061), new GeoCoordinates(53.254572, -6.742533)),
            UseDirection = true,
            Center = new GeoCoordinates(53.260876, -6.754061),
            Zoom = 17,
            Test = false
        };
        c159.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(53.257182, -6.744996), new GeoCoordinates(53.257101, -6.745384)) });
        tracks.Add(c159);

        var c160 = new CircuitConfiguration
        {
            Name = "Parc Mondello-Court Circuit",
            Code = "160",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(53.260876, -6.754061), new GeoCoordinates(53.254572, -6.742533)),
            UseDirection = true,
            Center = new GeoCoordinates(53.260876, -6.754061),
            Zoom = 17,
            Test = false
        };
        c160.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(53.257182, -6.744996), new GeoCoordinates(53.257101, -6.745384)) });
        tracks.Add(c160);

        var c161 = new CircuitConfiguration
        {
            Name = "Abruzzo International Circuit",
            Code = "161",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(42.306220, 14.376634), new GeoCoordinates(42.304096, 14.380611)),
            UseDirection = true,
            Center = new GeoCoordinates(42.306220, 14.376634),
            Zoom = 17,
            Test = false
        };
        c161.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(42.305412, 14.380073), new GeoCoordinates(42.305334, 14.379731)) });
        tracks.Add(c161);

        var c162 = new CircuitConfiguration
        {
            Name = "Autodromo Del Levante Binetto",
            Code = "162",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(40.997073, 16.739478), new GeoCoordinates(40.992218, 16.744146)),
            UseDirection = true,
            Center = new GeoCoordinates(40.997073, 16.739478),
            Zoom = 17,
            Test = false
        };
        c162.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(40.995051, 16.743305), new GeoCoordinates(40.995151, 16.742964)) });
        tracks.Add(c162);

        var c163 = new CircuitConfiguration
        {
            Name = "Circuit Autodromo Franciacorta",
            Code = "163",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(45.514247, 10.000874), new GeoCoordinates(45.508845, 10.010577)),
            UseDirection = true,
            Center = new GeoCoordinates(45.514247, 10.000874),
            Zoom = 17,
            Test = false
        };
        c163.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(45.513400, 10.005349), new GeoCoordinates(45.513057, 10.005371)) });
        tracks.Add(c163);

        var c164 = new CircuitConfiguration
        {
            Name = "Imola",
            Code = "164",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(44.345411, 11.700919), new GeoCoordinates(44.335395, 11.726418)),
            UseDirection = true,
            Center = new GeoCoordinates(44.345411, 11.700919),
            Zoom = 17,
            Test = false
        };
        c164.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(44.343866, 11.716935), new GeoCoordinates(44.344297, 11.716935)) });
        tracks.Add(c164);

        var c165 = new CircuitConfiguration
        {
            Name = "Kartodrome Is Arenadas",
            Code = "165",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(39.957685, 9.633764), new GeoCoordinates(39.954360, 9.636281)),
            UseDirection = true,
            Center = new GeoCoordinates(39.957685, 9.633764),
            Zoom = 17,
            Test = false
        };
        c165.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(39.956468, 9.634715), new GeoCoordinates(39.956541, 9.634956)) });
        tracks.Add(c165);

        var c166 = new CircuitConfiguration
        {
            Name = "Kart Planet Busca",
            Code = "166",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(44.496786, 7.501161), new GeoCoordinates(44.493912, 7.505539)),
            UseDirection = true,
            Center = new GeoCoordinates(44.496786, 7.501161),
            Zoom = 17,
            Test = false
        };
        c166.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(44.495665, 7.503971), new GeoCoordinates(44.495625, 7.503680)) });
        tracks.Add(c166);

        var c167 = new CircuitConfiguration
        {
            Name = "Autodrome Lombardore",
            Code = "167",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(45.236988, 7.722887), new GeoCoordinates(45.235388, 7.726746)),
            UseDirection = true,
            Center = new GeoCoordinates(45.236988, 7.722887),
            Zoom = 17,
            Test = false
        };
        c167.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(45.236648, 7.724788), new GeoCoordinates(45.236885, 7.724765)) });
        tracks.Add(c167);

        var c168 = new CircuitConfiguration
        {
            Name = "Circuit de Misano",
            Code = "168",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(43.965154, 12.675336), new GeoCoordinates(43.957403, 12.691641)),
            UseDirection = true,
            Center = new GeoCoordinates(43.965154, 12.675336),
            Zoom = 17,
            Test = false
        };
        c168.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(43.962020, 12.684398), new GeoCoordinates(43.962203, 12.683853)) });
        tracks.Add(c168);

        var c169 = new CircuitConfiguration
        {
            Name = "Circuit de Monza",
            Code = "169",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(45.631821, 9.278334), new GeoCoordinates(45.609344, 9.300735)),
            UseDirection = true,
            Center = new GeoCoordinates(45.631821, 9.278334),
            Zoom = 17,
            Test = false
        };
        c169.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(45.618984, 9.280897), new GeoCoordinates(45.618939, 9.281487)) });
        tracks.Add(c169);

        var c170 = new CircuitConfiguration
        {
            Name = "Autodromo Mores",
            Code = "170",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(40.510047, 8.829166), new GeoCoordinates(40.507273, 8.835911)),
            UseDirection = true,
            Center = new GeoCoordinates(40.510047, 8.829166),
            Zoom = 17,
            Test = false
        };
        c170.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(40.508433, 8.832027), new GeoCoordinates(40.508141, 8.832176)) });
        tracks.Add(c170);

        var c171 = new CircuitConfiguration
        {
            Name = "Circuit du Mugello",
            Code = "171",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(44.003888, 11.364773), new GeoCoordinates(43.989886, 11.378765)),
            UseDirection = true,
            Center = new GeoCoordinates(44.003888, 11.364773),
            Zoom = 17,
            Test = false
        };
        c171.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(43.997676, 11.371233), new GeoCoordinates(43.997526, 11.371697)) });
        tracks.Add(c171);

        var c172 = new CircuitConfiguration
        {
            Name = "Circuit de Naples-Sarno",
            Code = "172",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(40.838035, 14.564854), new GeoCoordinates(40.834015, 14.569064)),
            UseDirection = true,
            Center = new GeoCoordinates(40.838035, 14.564854),
            Zoom = 17,
            Test = false
        };
        c172.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(40.837748, 14.567685), new GeoCoordinates(40.837485, 14.567588)) });
        tracks.Add(c172);

        var c173 = new CircuitConfiguration
        {
            Name = "Pista Azzurra Jesolo",
            Code = "173",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(45.507596, 12.624302), new GeoCoordinates(45.505842, 12.628484)),
            UseDirection = true,
            Center = new GeoCoordinates(45.507596, 12.624302),
            Zoom = 17,
            Test = false
        };
        c173.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(45.506092, 12.626226), new GeoCoordinates(45.506259, 12.626118)) });
        tracks.Add(c173);

        var c174 = new CircuitConfiguration
        {
            Name = "Pista Due Mari",
            Code = "174",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(38.913887, 16.415609), new GeoCoordinates(38.912662, 16.419006)),
            UseDirection = true,
            Center = new GeoCoordinates(38.913887, 16.415609),
            Zoom = 17,
            Test = false
        };
        c174.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(38.913842, 16.416757), new GeoCoordinates(38.913631, 16.416779)) });
        tracks.Add(c174);

        var c175 = new CircuitConfiguration
        {
            Name = "Autodromo Riccardo Paletti-Varano",
            Code = "175",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(44.682202, 10.016331), new GeoCoordinates(44.679802, 10.029454)),
            UseDirection = true,
            Center = new GeoCoordinates(44.682202, 10.016331),
            Zoom = 17,
            Test = false
        };
        c175.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(44.680902, 10.021072), new GeoCoordinates(44.680526, 10.021050)) });
        tracks.Add(c175);

        var c176 = new CircuitConfiguration
        {
            Name = "Circuit international IL Sagittario",
            Code = "176",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(41.406033, 12.983090), new GeoCoordinates(41.403394, 12.987255)),
            UseDirection = true,
            Center = new GeoCoordinates(41.406033, 12.983090),
            Zoom = 17,
            Test = false
        };
        c176.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(41.405530, 12.984459), new GeoCoordinates(41.405753, 12.984346)) });
        tracks.Add(c176);

        var c177 = new CircuitConfiguration
        {
            Name = "Circuit complet de Tazio Nuvolari",
            Code = "177",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(45.076052, 8.986177), new GeoCoordinates(45.068964, 8.995394)),
            UseDirection = true,
            Center = new GeoCoordinates(45.076052, 8.986177),
            Zoom = 17,
            Test = false
        };
        c177.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(45.072451, 8.992017), new GeoCoordinates(45.072234, 8.991637)) });
        tracks.Add(c177);

        var c178 = new CircuitConfiguration
        {
            Name = "Autodrome de Vallelunga",
            Code = "178",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(42.166976, 12.365823), new GeoCoordinates(42.154503, 12.375569)),
            UseDirection = true,
            Center = new GeoCoordinates(42.166976, 12.365823),
            Zoom = 17,
            Test = false
        };
        c178.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(42.157662, 12.368750), new GeoCoordinates(42.157712, 12.369214)) });
        tracks.Add(c178);

        var c179 = new CircuitConfiguration
        {
            Name = "Autodrome Valle dei Templi",
            Code = "179",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(37.390100, 13.751195), new GeoCoordinates(37.382798, 13.756612)),
            UseDirection = true,
            Center = new GeoCoordinates(37.390100, 13.751195),
            Zoom = 17,
            Test = false
        };
        c179.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(37.385934, 13.752911), new GeoCoordinates(37.386000, 13.753248)) });
        tracks.Add(c179);

        var c180 = new CircuitConfiguration
        {
            Name = "Biķernieku trase",
            Code = "180",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(56.967196, 24.224018), new GeoCoordinates(56.963891, 24.230416)),
            UseDirection = true,
            Center = new GeoCoordinates(56.967196, 24.224018),
            Zoom = 17,
            Test = false
        };
        c180.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(56.965062, 24.229320), new GeoCoordinates(56.964883, 24.228666)) });
        tracks.Add(c180);

        var c181 = new CircuitConfiguration
        {
            Name = "Piste de karting de Madonas",
            Code = "181",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(56.866812, 26.221582), new GeoCoordinates(56.864842, 26.226416)),
            UseDirection = true,
            Center = new GeoCoordinates(56.866812, 26.221582),
            Zoom = 17,
            Test = false
        };
        c181.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(56.866152, 26.224960), new GeoCoordinates(56.866291, 26.225197)) });
        tracks.Add(c181);

        var c182 = new CircuitConfiguration
        {
            Name = "Nemuno Ziedas",
            Code = "182",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(54.925236, 23.700046), new GeoCoordinates(54.913706, 23.717550)),
            UseDirection = true,
            Center = new GeoCoordinates(54.925236, 23.700046),
            Zoom = 17,
            Test = false
        };
        c182.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(54.918018, 23.703192), new GeoCoordinates(54.917959, 23.703581)) });
        tracks.Add(c182);

        var c184 = new CircuitConfiguration
        {
            Name = "Kongsberg Motorsenter Gokart",
            Code = "184",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(59.669613, 9.693514), new GeoCoordinates(59.667243, 9.698220)),
            UseDirection = true,
            Center = new GeoCoordinates(59.669613, 9.693514),
            Zoom = 17,
            Test = false
        };
        c184.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(59.668481, 9.696883), new GeoCoordinates(59.668367, 9.696636)) });
        tracks.Add(c184);

        var c185 = new CircuitConfiguration
        {
            Name = "Kongsberg Motorsenter Gokart-Anti dans le sens des aiguies d'une montre",
            Code = "185",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(59.669613, 9.693515), new GeoCoordinates(59.667243, 9.698221)),
            UseDirection = true,
            Center = new GeoCoordinates(59.669613, 9.693515),
            Zoom = 17,
            Test = false
        };
        c185.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(59.668367, 9.696636), new GeoCoordinates(59.668481, 9.696883)) });
        tracks.Add(c185);

        var c186 = new CircuitConfiguration
        {
            Name = "Parc automobile de Rudskogen",
            Code = "186",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(59.370290, 11.253660), new GeoCoordinates(59.361205, 11.266043)),
            UseDirection = true,
            Center = new GeoCoordinates(59.370290, 11.253660),
            Zoom = 17,
            Test = false
        };
        c186.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(59.365700, 11.263559), new GeoCoordinates(59.365757, 11.262854)) });
        tracks.Add(c186);

        var c187 = new CircuitConfiguration
        {
            Name = "Circuit de Valerbanen",
            Code = "187",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(60.708335, 11.804190), new GeoCoordinates(60.699933, 11.819586)),
            UseDirection = true,
            Center = new GeoCoordinates(60.708335, 11.804190),
            Zoom = 17,
            Test = false
        };
        c187.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(60.701684, 11.813990), new GeoCoordinates(60.701799, 11.814408)) });
        tracks.Add(c187);

        var c188 = new CircuitConfiguration
        {
            Name = "Autodrom Slomczyn",
            Code = "188",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.879585, 20.924481), new GeoCoordinates(51.877304, 20.928963)),
            UseDirection = true,
            Center = new GeoCoordinates(51.879585, 20.924481),
            Zoom = 17,
            Test = false
        };
        c188.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.878791, 20.928256), new GeoCoordinates(51.878772, 20.927753)) });
        tracks.Add(c188);

        var c189 = new CircuitConfiguration
        {
            Name = "Autodrom Slomczyn autre sens",
            Code = "189",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.879585, 20.924482), new GeoCoordinates(51.877304, 20.928964)),
            UseDirection = true,
            Center = new GeoCoordinates(51.879585, 20.924482),
            Zoom = 17,
            Test = false
        };
        c189.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.878772, 20.927753), new GeoCoordinates(51.878791, 20.928256)) });
        tracks.Add(c189);

        var c190 = new CircuitConfiguration
        {
            Name = "Automobilklub Radom",
            Code = "190",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.418442, 21.148300), new GeoCoordinates(51.416652, 21.151336)),
            UseDirection = true,
            Center = new GeoCoordinates(51.418442, 21.148300),
            Zoom = 17,
            Test = false
        };
        c190.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.417027, 21.149403), new GeoCoordinates(51.417220, 21.149537)) });
        tracks.Add(c190);

        var c191 = new CircuitConfiguration
        {
            Name = "Tor Poznań",
            Code = "191",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.420809, 16.799190), new GeoCoordinates(52.413833, 16.814983)),
            UseDirection = true,
            Center = new GeoCoordinates(52.420809, 16.799190),
            Zoom = 17,
            Test = false
        };
        c191.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.420393, 16.806443), new GeoCoordinates(52.420043, 16.806447)) });
        tracks.Add(c191);

        var c192 = new CircuitConfiguration
        {
            Name = "Centre de course WallraV",
            Code = "192",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.940331, 15.598063), new GeoCoordinates(51.939079, 15.603335)),
            UseDirection = true,
            Center = new GeoCoordinates(51.940331, 15.598063),
            Zoom = 17,
            Test = false
        };
        c192.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.939189, 15.600027), new GeoCoordinates(51.939420, 15.599935)) });
        tracks.Add(c192);

        var c193 = new CircuitConfiguration
        {
            Name = "Algarve International Circuit-Portimao",
            Code = "193",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(37.236918, -8.633089), new GeoCoordinates(37.225305, -8.622443)),
            UseDirection = true,
            Center = new GeoCoordinates(37.236918, -8.633089),
            Zoom = 17,
            Test = false
        };
        c193.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(37.232206, -8.631124), new GeoCoordinates(37.232333, -8.630577)) });
        tracks.Add(c193);

        var c194 = new CircuitConfiguration
        {
            Name = "Kartodromo de Baltar",
            Code = "194",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(41.188413, -8.397173), new GeoCoordinates(41.185867, -8.394453)),
            UseDirection = true,
            Center = new GeoCoordinates(41.188413, -8.397173),
            Zoom = 17,
            Test = false
        };
        c194.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(41.187847, -8.395638), new GeoCoordinates(41.187709, -8.395815)) });
        tracks.Add(c194);

        var c195 = new CircuitConfiguration
        {
            Name = "Circuit of Braga",
            Code = "195",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(41.589866, -8.452492), new GeoCoordinates(41.583075, -8.437551)),
            UseDirection = true,
            Center = new GeoCoordinates(41.589866, -8.452492),
            Zoom = 17,
            Test = false
        };
        c195.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(41.588109, -8.443046), new GeoCoordinates(41.588343, -8.443197)) });
        tracks.Add(c195);

        var c196 = new CircuitConfiguration
        {
            Name = "Cabo do Mundo",
            Code = "196",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(41.223913, -8.715046), new GeoCoordinates(41.222924, -8.711410)),
            UseDirection = true,
            Center = new GeoCoordinates(41.223913, -8.715046),
            Zoom = 17,
            Test = false
        };
        c196.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(41.223747, -8.713671), new GeoCoordinates(41.223621, -8.713571)) });
        tracks.Add(c196);

        var c197 = new CircuitConfiguration
        {
            Name = "kiro Karting-Bombarral",
            Code = "197",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(39.267995, -9.191063), new GeoCoordinates(39.265667, -9.188260)),
            UseDirection = true,
            Center = new GeoCoordinates(39.267995, -9.191063),
            Zoom = 17,
            Test = false
        };
        c197.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(39.266409, -9.188695), new GeoCoordinates(39.266451, -9.188972)) });
        tracks.Add(c197);

        var c198 = new CircuitConfiguration
        {
            Name = "Kartodrome de Viana",
            Code = "198",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(41.651000, -8.800624), new GeoCoordinates(41.648896, -8.796494)),
            UseDirection = true,
            Center = new GeoCoordinates(41.651000, -8.800624),
            Zoom = 17,
            Test = false
        };
        c198.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(41.650611, -8.799415), new GeoCoordinates(41.650425, -8.799236)) });
        tracks.Add(c198);

        var c199 = new CircuitConfiguration
        {
            Name = "Circuit Anglesey-Club",
            Code = "199",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(53.191800, -4.497816), new GeoCoordinates(53.187078, -4.491294)),
            UseDirection = true,
            Center = new GeoCoordinates(53.191800, -4.497816),
            Zoom = 17,
            Test = false
        };
        c199.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(53.188586, -4.496454), new GeoCoordinates(53.188256, -4.496358)) });
        tracks.Add(c199);

        var c200 = new CircuitConfiguration
        {
            Name = "Circuit Anglesey-Côtier",
            Code = "200",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(53.191948, -4.504037), new GeoCoordinates(53.187309, -4.491032)),
            UseDirection = true,
            Center = new GeoCoordinates(53.191948, -4.504037),
            Zoom = 17,
            Test = false
        };
        c200.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(53.188586, -4.496455), new GeoCoordinates(53.188256, -4.496359)) });
        tracks.Add(c200);

        var c201 = new CircuitConfiguration
        {
            Name = "Circuit Anglesey-GP",
            Code = "201",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(53.193110, -4.504118), new GeoCoordinates(53.187269, -4.491158)),
            UseDirection = true,
            Center = new GeoCoordinates(53.193110, -4.504118),
            Zoom = 17,
            Test = false
        };
        c201.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(53.188586, -4.496456), new GeoCoordinates(53.188256, -4.496360)) });
        tracks.Add(c201);

        var c202 = new CircuitConfiguration
        {
            Name = "Anglesey-Circuit national",
            Code = "202",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(53.193117, -4.504197), new GeoCoordinates(53.187269, -4.491159)),
            UseDirection = true,
            Center = new GeoCoordinates(53.193117, -4.504197),
            Zoom = 17,
            Test = false
        };
        c202.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(53.189793, -4.497483), new GeoCoordinates(53.189895, -4.497942)) });
        tracks.Add(c202);

        var c203 = new CircuitConfiguration
        {
            Name = "Circuit de kart de Bayford Meadows",
            Code = "203",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.348919, 0.744008), new GeoCoordinates(51.347095, 0.746683)),
            UseDirection = true,
            Center = new GeoCoordinates(51.348919, 0.744008),
            Zoom = 17,
            Test = false
        };
        c203.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.347724, 0.745679), new GeoCoordinates(51.347786, 0.745510)) });
        tracks.Add(c203);

        var c204 = new CircuitConfiguration
        {
            Name = "Bedford Autodrome-GT Circuit",
            Code = "204",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.238766, -0.486640), new GeoCoordinates(52.229210, -0.456737)),
            UseDirection = true,
            Center = new GeoCoordinates(52.238766, -0.486640),
            Zoom = 17,
            Test = false
        };
        c204.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.235266, -0.474441), new GeoCoordinates(52.235026, -0.474156)) });
        tracks.Add(c204);

        var c205 = new CircuitConfiguration
        {
            Name = "Bedford Autodrome-SW Circuit",
            Code = "205",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.238766, -0.486641), new GeoCoordinates(52.228809, -0.462013)),
            UseDirection = true,
            Center = new GeoCoordinates(52.238766, -0.486641),
            Zoom = 17,
            Test = false
        };
        c205.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.235266, -0.474442), new GeoCoordinates(52.235026, -0.474157)) });
        tracks.Add(c205);

        var c206 = new CircuitConfiguration
        {
            Name = "Circuit GP de Brands Hatch",
            Code = "206",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.361365, 0.255399), new GeoCoordinates(51.351874, 0.269092)),
            UseDirection = true,
            Center = new GeoCoordinates(51.361365, 0.255399),
            Zoom = 17,
            Test = false
        };
        c206.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.360348, 0.260040), new GeoCoordinates(51.360110, 0.260198)) });
        tracks.Add(c206);

        var c207 = new CircuitConfiguration
        {
            Name = "Circuit de course de Bishopscourt",
            Code = "207",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(54.314389, -5.586714), new GeoCoordinates(54.300577, -5.577239)),
            UseDirection = true,
            Center = new GeoCoordinates(54.314389, -5.586714),
            Zoom = 17,
            Test = false
        };
        c207.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(54.303317, -5.581996), new GeoCoordinates(54.303311, -5.581535)) });
        tracks.Add(c207);

        var c208 = new CircuitConfiguration
        {
            Name = "Parc Blyton",
            Code = "208",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(53.462876, -0.698617), new GeoCoordinates(53.452430, -0.686258)),
            UseDirection = true,
            Center = new GeoCoordinates(53.462876, -0.698617),
            Zoom = 17,
            Test = false
        };
        c208.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(53.460026, -0.688886), new GeoCoordinates(53.459986, -0.688403)) });
        tracks.Add(c208);

        var c209 = new CircuitConfiguration
        {
            Name = "Circuit industriel de Brands Hatch",
            Code = "209",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.361365, 0.255399), new GeoCoordinates(51.356824, 0.266019)),
            UseDirection = true,
            Center = new GeoCoordinates(51.361365, 0.255399),
            Zoom = 17,
            Test = false
        };
        c209.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(53.460026, -0.688887), new GeoCoordinates(53.459986, -0.688404)) });
        tracks.Add(c209);

        var c210 = new CircuitConfiguration
        {
            Name = "Circuit de karting du parc Buckmore",
            Code = "210",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.344077, 0.499349), new GeoCoordinates(51.341922, 0.504063)),
            UseDirection = true,
            Center = new GeoCoordinates(51.344077, 0.499349),
            Zoom = 17,
            Test = false
        };
        c210.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.342491, 0.501175), new GeoCoordinates(51.342610, 0.501002)) });
        tracks.Add(c210);

        var c211 = new CircuitConfiguration
        {
            Name = "Parc Cadwell",
            Code = "211",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(53.314598, -0.072068), new GeoCoordinates(53.304123, -0.055353)),
            UseDirection = true,
            Center = new GeoCoordinates(53.314598, -0.072068),
            Zoom = 17,
            Test = false
        };
        c211.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(53.310205, -0.059212), new GeoCoordinates(53.310338, -0.059733)) });
        tracks.Add(c211);

        var c212 = new CircuitConfiguration
        {
            Name = "Circuit de Castle Combe",
            Code = "212",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.493956, -2.220132), new GeoCoordinates(51.484123, -2.200275)),
            UseDirection = true,
            Center = new GeoCoordinates(51.493956, -2.220132),
            Zoom = 17,
            Test = false
        };
        c212.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.492932, -2.216107), new GeoCoordinates(51.492514, -2.215924)) });
        tracks.Add(c212);

        var cnandrintest5 = new CircuitConfiguration
        {
            Name = "Nandrin Faftu",
            Code = "nandrintest5",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.510709, 5.416930), new GeoCoordinates(50.496495, 5.444712)),
            UseDirection = true,
            Center = new GeoCoordinates(50.510709, 5.416930),
            Zoom = 17,
            Test = false
        };
        cnandrintest5.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.506814, 5.420303), new GeoCoordinates(50.506738, 5.419935)) });
        cnandrintest5.Segments.Add(new CircuitSegment() { Number = 2, Boundary = new CircuitGeoLine(new GeoCoordinates(50.499416, 5.438124), new GeoCoordinates(50.499192, 5.438439)) });
        cnandrintest5.Segments.Add(new CircuitSegment() { Number = 3, Boundary = new CircuitGeoLine(new GeoCoordinates(50.506894, 5.436340), new GeoCoordinates(50.507021, 5.436778)) });
        tracks.Add(cnandrintest5);

        var c214 = new CircuitConfiguration
        {
            Name = "Croft Circuit",
            Code = "214",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(54.459356, -1.566100), new GeoCoordinates(54.451001, -1.549153)),
            UseDirection = true,
            Center = new GeoCoordinates(54.459356, -1.566100),
            Zoom = 17,
            Test = false
        };
        c214.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(54.455437, -1.555830), new GeoCoordinates(54.455355, -1.555255)) });
        tracks.Add(c214);

        var c215 = new CircuitConfiguration
        {
            Name = "Darley Moor",
            Code = "215",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.982050, -1.749367), new GeoCoordinates(52.975364, -1.732949)),
            UseDirection = true,
            Center = new GeoCoordinates(52.982050, -1.749367),
            Zoom = 17,
            Test = false
        };
        c215.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.977222, -1.747405), new GeoCoordinates(52.977177, -1.746855)) });
        tracks.Add(c215);

        var c216 = new CircuitConfiguration
        {
            Name = "DaytonaMilton Keynes",
            Code = "216",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.040972, -0.785478), new GeoCoordinates(52.038490, -0.781008)),
            UseDirection = true,
            Center = new GeoCoordinates(52.040972, -0.785478),
            Zoom = 17,
            Test = false
        };
        c216.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.040277, -0.784560), new GeoCoordinates(52.040353, -0.784744)) });
        tracks.Add(c216);

        var c217 = new CircuitConfiguration
        {
            Name = "Parc de Daytona Sandown",
            Code = "217",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.376780, -0.362772), new GeoCoordinates(51.375313, -0.358672)),
            UseDirection = true,
            Center = new GeoCoordinates(51.376780, -0.362772),
            Zoom = 17,
            Test = false
        };
        c217.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.376298, -0.361798), new GeoCoordinates(51.376133, -0.361701)) });
        tracks.Add(c217);

        var c218 = new CircuitConfiguration
        {
            Name = "Daytona Tamworth",
            Code = "218",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.623889, -1.642977), new GeoCoordinates(52.621697, -1.639057)),
            UseDirection = true,
            Center = new GeoCoordinates(52.623889, -1.642977),
            Zoom = 17,
            Test = false
        };
        c218.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.622733, -1.642337), new GeoCoordinates(52.622901, -1.642257)) });
        tracks.Add(c218);

        var c219 = new CircuitConfiguration
        {
            Name = "Parc de Donington",
            Code = "219",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.834138, -1.385926), new GeoCoordinates(52.826787, -1.361593)),
            UseDirection = true,
            Center = new GeoCoordinates(52.834138, -1.385926),
            Zoom = 17,
            Test = false
        };
        c219.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.829709, -1.379583), new GeoCoordinates(52.829982, -1.379485)) });
        tracks.Add(c219);

        var c220 = new CircuitConfiguration
        {
            Name = "Parc national de Donington",
            Code = "220",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.834138, -1.385926), new GeoCoordinates(52.826787, -1.361593)),
            UseDirection = true,
            Center = new GeoCoordinates(52.834138, -1.385926),
            Zoom = 17,
            Test = false
        };
        c220.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.829709, -1.379583), new GeoCoordinates(52.829982, -1.379485)) });
        tracks.Add(c220);

        var c221 = new CircuitConfiguration
        {
            Name = "Parc Dunsfold",
            Code = "221",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.120065, -0.550202), new GeoCoordinates(51.112925, -0.532412)),
            UseDirection = true,
            Center = new GeoCoordinates(51.120065, -0.550202),
            Zoom = 17,
            Test = false
        };
        c221.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.118544, -0.536334), new GeoCoordinates(51.118821, -0.536487)) });
        tracks.Add(c221);

        var c222 = new CircuitConfiguration
        {
            Name = "Circuit moteur de Goodwood",
            Code = "222",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.865155, -0.769086), new GeoCoordinates(50.852661, -0.748547)),
            UseDirection = true,
            Center = new GeoCoordinates(50.865155, -0.769086),
            Zoom = 17,
            Test = false
        };
        c222.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.857976, -0.752282), new GeoCoordinates(50.857931, -0.752904)) });
        tracks.Add(c222);

        var c223 = new CircuitConfiguration
        {
            Name = "Karting North East",
            Code = "223",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(54.849542, -1.425562), new GeoCoordinates(54.847651, -1.420381)),
            UseDirection = true,
            Center = new GeoCoordinates(54.849542, -1.425562),
            Zoom = 17,
            Test = false
        };
        c223.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(54.848140, -1.422817), new GeoCoordinates(54.848310, -1.422946)) });
        tracks.Add(c223);

        var c224 = new CircuitConfiguration
        {
            Name = "Circuit de course de Knockhill",
            Code = "224",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(56.131728, -3.513329), new GeoCoordinates(56.126455, -3.496935)),
            UseDirection = true,
            Center = new GeoCoordinates(56.131728, -3.513329),
            Zoom = 17,
            Test = false
        };
        c224.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(56.131133, -3.508120), new GeoCoordinates(56.130816, -3.508185)) });
        tracks.Add(c224);

        var c225 = new CircuitConfiguration
        {
            Name = "Circuit de course de Lydden Hill",
            Code = "225",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.180193, 1.194117), new GeoCoordinates(51.176217, 1.202625)),
            UseDirection = true,
            Center = new GeoCoordinates(51.180193, 1.194117),
            Zoom = 17,
            Test = false
        };
        c225.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.177405, 1.197629), new GeoCoordinates(51.177669, 1.197812)) });
        tracks.Add(c225);

        var c226 = new CircuitConfiguration
        {
            Name = "Llandow Circuit-Anticlockwise",
            Code = "226",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.433289, -3.502403), new GeoCoordinates(51.430016, -3.492553)),
            UseDirection = true,
            Center = new GeoCoordinates(51.433289, -3.502403),
            Zoom = 17,
            Test = false
        };
        c226.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.432360, -3.495498), new GeoCoordinates(51.432595, -3.495512)) });
        tracks.Add(c226);

        var c227 = new CircuitConfiguration
        {
            Name = "Llandow Circuit-Clockwise",
            Code = "227",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.433289, -3.502404), new GeoCoordinates(51.430016, -3.492554)),
            UseDirection = true,
            Center = new GeoCoordinates(51.433289, -3.502404),
            Zoom = 17,
            Test = false
        };
        c227.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.432595, -3.495512), new GeoCoordinates(51.432360, -3.495498)) });
        tracks.Add(c227);

        var c228 = new CircuitConfiguration
        {
            Name = "Lydd Kart Circuit",
            Code = "228",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.937082, 0.905681), new GeoCoordinates(50.933291, 0.908600)),
            UseDirection = true,
            Center = new GeoCoordinates(50.937082, 0.905681),
            Zoom = 17,
            Test = false
        };
        c228.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.934499, 0.907662), new GeoCoordinates(50.934501, 0.907891)) });
        tracks.Add(c228);

        var c229 = new CircuitConfiguration
        {
            Name = "Parc Mallory",
            Code = "229",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.602237, -1.342291), new GeoCoordinates(52.594480, -1.331731)),
            UseDirection = true,
            Center = new GeoCoordinates(52.602237, -1.342291),
            Zoom = 17,
            Test = false
        };
        c229.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.598673, -1.336697), new GeoCoordinates(52.598668, -1.337222)) });
        tracks.Add(c229);

        var c230 = new CircuitConfiguration
        {
            Name = "Circuit de karting Mansell Raceway",
            Code = "230",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(50.868483, -3.240611), new GeoCoordinates(50.864753, -3.236815)),
            UseDirection = true,
            Center = new GeoCoordinates(50.868483, -3.240611),
            Zoom = 17,
            Test = false
        };
        c230.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(50.867361, -3.238629), new GeoCoordinates(50.867291, -3.238333)) });
        tracks.Add(c230);

        var c231 = new CircuitConfiguration
        {
            Name = "Oulton Park-Fosters Circuit",
            Code = "231",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(53.183829, -2.623535), new GeoCoordinates(53.173811, -2.609330)),
            UseDirection = true,
            Center = new GeoCoordinates(53.183829, -2.623535),
            Zoom = 17,
            Test = false
        };
        c231.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(53.180052, -2.612516), new GeoCoordinates(53.179994, -2.613103)) });
        tracks.Add(c231);

        var c232 = new CircuitConfiguration
        {
            Name = "Oulton Park-International Circuit",
            Code = "232",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(53.183401, -2.623345), new GeoCoordinates(53.169803, -2.607790)),
            UseDirection = true,
            Center = new GeoCoordinates(53.183401, -2.623345),
            Zoom = 17,
            Test = false
        };
        c232.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(53.180052, -2.612517), new GeoCoordinates(53.179994, -2.613104)) });
        tracks.Add(c232);

        var c233 = new CircuitConfiguration
        {
            Name = "Oulton Park-Island Circuit",
            Code = "233",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(53.183401, -2.623346), new GeoCoordinates(53.169803, -2.607791)),
            UseDirection = true,
            Center = new GeoCoordinates(53.183401, -2.623346),
            Zoom = 17,
            Test = false
        };
        c233.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(53.180052, -2.612518), new GeoCoordinates(53.179994, -2.613105)) });
        tracks.Add(c233);

        var c234 = new CircuitConfiguration
        {
            Name = "Pembrey Circuit",
            Code = "234",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.709614, -4.327304), new GeoCoordinates(51.701755, -4.314086)),
            UseDirection = true,
            Center = new GeoCoordinates(51.709614, -4.327304),
            Zoom = 17,
            Test = false
        };
        c234.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.705966, -4.324101), new GeoCoordinates(51.705824, -4.323656)) });
        tracks.Add(c234);

        var c235 = new CircuitConfiguration
        {
            Name = "PFI Kart Circuit",
            Code = "235",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(53.040546, -0.663784), new GeoCoordinates(53.036062, -0.658996)),
            UseDirection = true,
            Center = new GeoCoordinates(53.040546, -0.663784),
            Zoom = 17,
            Test = false
        };
        c235.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(53.038081, -0.660969), new GeoCoordinates(53.038137, -0.661190)) });
        tracks.Add(c235);

        var c236 = new CircuitConfiguration
        {
            Name = "Prestwold Hall",
            Code = "236",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.799289, -1.131904), new GeoCoordinates(52.789637, -1.116556)),
            UseDirection = true,
            Center = new GeoCoordinates(52.799289, -1.131904),
            Zoom = 17,
            Test = false
        };
        c236.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.793173, -1.130259), new GeoCoordinates(52.793065, -1.129792)) });
        tracks.Add(c236);

        var c237 = new CircuitConfiguration
        {
            Name = "Prodrive Track",
            Code = "237",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.364911, -1.681068), new GeoCoordinates(52.349204, -1.653016)),
            UseDirection = true,
            Center = new GeoCoordinates(52.364911, -1.681068),
            Zoom = 17,
            Test = false
        };
        c237.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.360492, -1.658017), new GeoCoordinates(52.360679, -1.658320)) });
        tracks.Add(c237);

        var c238 = new CircuitConfiguration
        {
            Name = "Rockingham-International Sportscar Circuit",
            Code = "238",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.519247, -0.664337), new GeoCoordinates(52.511205, -0.648555)),
            UseDirection = true,
            Center = new GeoCoordinates(52.519247, -0.664337),
            Zoom = 17,
            Test = false
        };
        c238.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.514823, -0.662038), new GeoCoordinates(52.514700, -0.662637)) });
        tracks.Add(c238);

        var c239 = new CircuitConfiguration
        {
            Name = "Rockingham-National Circuit-Anticlockwise",
            Code = "239",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.519247, -0.664338), new GeoCoordinates(52.511205, -0.648556)),
            UseDirection = true,
            Center = new GeoCoordinates(52.519247, -0.664338),
            Zoom = 17,
            Test = false
        };
        c239.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.514823, -0.662039), new GeoCoordinates(52.514700, -0.662638)) });
        tracks.Add(c239);

        var c240 = new CircuitConfiguration
        {
            Name = "Rockingham-National Circuit-Clockwise",
            Code = "240",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.519247, -0.664339), new GeoCoordinates(52.511205, -0.648557)),
            UseDirection = true,
            Center = new GeoCoordinates(52.519247, -0.664339),
            Zoom = 17,
            Test = false
        };
        c240.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.514700, -0.662638), new GeoCoordinates(52.514823, -0.662039)) });
        tracks.Add(c240);

        var c241 = new CircuitConfiguration
        {
            Name = "Rockingham-Oval Circuit",
            Code = "241",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(52.519247, -0.664340), new GeoCoordinates(52.511205, -0.648558)),
            UseDirection = true,
            Center = new GeoCoordinates(52.519247, -0.664340),
            Zoom = 17,
            Test = false
        };
        c241.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(52.514824, -0.662033), new GeoCoordinates(52.514702, -0.662638)) });
        tracks.Add(c241);

        var c242 = new CircuitConfiguration
        {
            Name = "Rye House Kart Raceway",
            Code = "242",
            Type = CircuitType.Closed,
            BoundingBox = new CircuitGeoLine(new GeoCoordinates(51.768277, 0.009830), new GeoCoordinates(51.766538, 0.013564)),
            UseDirection = true,
            Center = new GeoCoordinates(51.768277, 0.009830),
            Zoom = 17,
            Test = false
        };
        c242.Segments.Add(new CircuitSegment() { Number = 1, Boundary = new CircuitGeoLine(new GeoCoordinates(51.767359, 0.010760), new GeoCoordinates(51.767511, 0.010962)) });
        tracks.Add(c242);

        return tracks;
    }
}
