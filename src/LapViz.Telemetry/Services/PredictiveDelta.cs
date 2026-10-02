using System;
using System.Collections.Generic;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Services;

/// <summary>
/// Predictive delta: how far ahead of or behind the best lap the driver is, at the same place
/// on the track, updated at every fix. Same algorithm and constants as a LapViz device
/// (LapViz.Devices, lapviz_device/src/core/predictive_delta.h), so that the app and the device
/// show the same delta.
/// </summary>
/// <remarks>
/// <para>
/// The best lap is kept as a trace: its positions and the time since the start of the lap at
/// each one. At a fix of the current lap, the nearest point of that trace (projected on its
/// segments, searched a little behind and ahead of the previous match so that it follows the
/// progression) gives the time the best lap had there; the delta is the elapsed time minus that
/// time. Negative = ahead.
/// </para>
/// <para>
/// A lap becomes the reference when it is faster than the reference and complete: started at a
/// start/finish crossing, no gap of more than <see cref="MaxFixGap"/> between two fixes, and
/// fitting the trace capacity. Lap times are those of the start/finish crossings, so the
/// reference lap time is the best lap time of <see cref="LapTimerService"/>.
/// </para>
/// </remarks>
public sealed class PredictiveDelta
{
    /// <summary>A longer gap between two fixes (GPS lost) makes the lap unusable as a reference.</summary>
    public static readonly TimeSpan MaxFixGap = TimeSpan.FromSeconds(1);

    /// <summary>A fix farther than this from the reference trace (pit lane, off track) has no delta.</summary>
    public const double MaxDistanceMeters = 25;

    /// <summary>Segments of the reference searched behind the last match.</summary>
    public const int SearchBehind = 3;

    /// <summary>Segments of the reference searched ahead of the last match (about 5 s at 10 Hz).</summary>
    public const int SearchAhead = 50;

    // 1 degree of latitude in meters (1e-7 degree = 1.11 cm, as the device)
    private const double MetersPerDegree = 111319;

    private readonly int _capacity;

    private List<TracePoint> _best = new List<TracePoint>();
    private List<TracePoint> _current = new List<TracePoint>();
    private TimeSpan _bestLapTime;

    private bool _inLap;
    private bool _currentValid;
    private DateTimeOffset _lapStart;
    private DateTimeOffset _lastFix;
    private double _cosLatitude = 1;
    private int _matchIndex;

    /// <summary>
    /// Creates a predictive delta keeping at most <paramref name="capacity"/> points per lap
    /// (15000: a 10 min lap at 25 Hz). A longer lap cannot become the reference.
    /// </summary>
    public PredictiveDelta(int capacity = 15000)
    {
        if (capacity < 2)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Must be >= 2.");

        _capacity = capacity;
    }

    /// <summary>True when a reference lap exists.</summary>
    public bool HasReference => _best.Count >= 2;

    /// <summary>Time of the reference lap (zero without reference).</summary>
    public TimeSpan ReferenceLapTime => _bestLapTime;

    /// <summary>Delta at the last fix (negative = ahead of the reference), null if it could not be computed.</summary>
    public TimeSpan? Delta { get; private set; }

    /// <summary>Forgets the laps and the reference (circuit changed).</summary>
    public void Reset()
    {
        _inLap = false;
        _current.Clear();
        _best.Clear();
        _bestLapTime = TimeSpan.Zero;
        Delta = null;
    }

    /// <summary>
    /// Start/finish crossed at <paramref name="time"/>, at <paramref name="position"/>: ends the
    /// current lap, which may become the reference, and starts a new one.
    /// </summary>
    public void OnLapStart(GeoCoordinates position, DateTimeOffset time)
    {
        if (position == null)
            throw new ArgumentNullException(nameof(position));

        if (_inLap && _currentValid && time > _lapStart)
        {
            Append(position, time);

            var lapTime = time - _lapStart;
            if (_currentValid && (_best.Count == 0 || lapTime < _bestLapTime))
            {
                var swap = _best;
                _best = _current;
                _current = swap;
                _bestLapTime = lapTime;
            }
        }

        _inLap = true;
        _currentValid = true;
        _lapStart = time;
        _lastFix = time;
        _current.Clear();
        _matchIndex = 0;
        Delta = null;
        _cosLatitude = Math.Cos(position.Latitude * Math.PI / 180.0);

        Append(position, time);
    }

    /// <summary>
    /// Starts a lap from a Lap event of <see cref="LapTimerService"/>, at the interpolated
    /// crossing point. Other events are ignored.
    /// </summary>
    public void OnLapEvent(SessionDataEvent lapEvent)
    {
        if (lapEvent == null || lapEvent.Type != SessionEventType.Lap)
            return;

        OnLapStart(CrossingPosition(lapEvent), lapEvent.Timestamp);
    }

    /// <summary>A fix of the current lap.</summary>
    public void OnFix(GeoCoordinates position, DateTimeOffset time)
    {
        if (position == null)
            throw new ArgumentNullException(nameof(position));

        if (!_inLap || time <= _lastFix)
            return;

        if (time - _lastFix > MaxFixGap)
            _currentValid = false;

        _lastFix = time;
        Append(position, time);
        UpdateDelta(position, (time - _lapStart).TotalMilliseconds);
    }

    /// <summary>Point of the trajectory where the line was crossed (interpolated with the event factor).</summary>
    private static GeoCoordinates CrossingPosition(SessionDataEvent crossing)
    {
        var from = crossing.FirstGeoCoordinates;
        var to = crossing.SecondGeoCoordinates;
        if (from == null)
            return to;
        if (to == null)
            return from;

        var f = crossing.Factor;
        return new GeoCoordinates(
            from.Latitude + ((to.Latitude - from.Latitude) * f),
            from.Longitude + ((to.Longitude - from.Longitude) * f));
    }

    private void Append(GeoCoordinates position, DateTimeOffset time)
    {
        if (!_currentValid)
            return;

        if (_current.Count >= _capacity)
        {
            _currentValid = false;
            return;
        }

        _current.Add(new TracePoint(position.Latitude, position.Longitude, (time - _lapStart).TotalMilliseconds));
    }

    /// <summary>Local meters from <paramref name="origin"/> to the point (x east, y north).</summary>
    private void ToMeters(TracePoint origin, double latitude, double longitude, out double x, out double y)
    {
        y = (latitude - origin.Latitude) * MetersPerDegree;
        x = (longitude - origin.Longitude) * MetersPerDegree * _cosLatitude;
    }

    /// <summary>Squared distance from the position to segment <paramref name="index"/> of the reference, and where it projects.</summary>
    private double SegmentDistance(int index, GeoCoordinates position, out double fraction)
    {
        var a = _best[index];
        var b = _best[index + 1];
        ToMeters(a, b.Latitude, b.Longitude, out var bx, out var by);
        ToMeters(a, position.Latitude, position.Longitude, out var px, out var py);

        var length2 = (bx * bx) + (by * by);
        fraction = length2 > 0 ? ((px * bx) + (py * by)) / length2 : 0;
        fraction = fraction < 0 ? 0 : fraction > 1 ? 1 : fraction;

        var dx = px - (fraction * bx);
        var dy = py - (fraction * by);
        return (dx * dx) + (dy * dy);
    }

    /// <summary>Nearest segment in [first, last], or false if none is within <see cref="MaxDistanceMeters"/>.</summary>
    private bool Nearest(int first, int last, GeoCoordinates position, out int index, out double fraction)
    {
        var best = MaxDistanceMeters * MaxDistanceMeters;
        var found = false;
        index = 0;
        fraction = 0;

        for (var i = first; i <= last; i++)
        {
            var distance = SegmentDistance(i, position, out var f);
            if (distance <= best)
            {
                best = distance;
                index = i;
                fraction = f;
                found = true;
            }
        }

        return found;
    }

    private void UpdateDelta(GeoCoordinates position, double elapsedMs)
    {
        Delta = null;

        if (_best.Count < 2)
            return;

        var lastSegment = _best.Count - 2;
        var first = Math.Max(0, _matchIndex - SearchBehind);
        var last = Math.Min(lastSegment, _matchIndex + SearchAhead);

        // Near the last match first; if the driver is lost (back from the pit lane, GPS gap),
        // anywhere on the reference
        if (!Nearest(first, last, position, out var index, out var fraction) &&
            !Nearest(0, lastSegment, position, out index, out fraction))
        {
            return;
        }

        _matchIndex = index;

        var t0 = _best[index].TimeMs;
        var t1 = _best[index + 1].TimeMs;
        Delta = TimeSpan.FromTicks((long)Math.Round((elapsedMs - (t0 + ((t1 - t0) * fraction))) * TimeSpan.TicksPerMillisecond));
    }

    private readonly struct TracePoint
    {
        public TracePoint(double latitude, double longitude, double timeMs)
        {
            Latitude = latitude;
            Longitude = longitude;
            TimeMs = timeMs;
        }

        public double Latitude { get; }

        public double Longitude { get; }

        // Time since the start of the lap
        public double TimeMs { get; }
    }
}
