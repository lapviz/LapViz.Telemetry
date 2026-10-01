using System;
using System.Linq;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Services;

/// <summary>
/// Sector crossing detection and lap derivation shared by <see cref="LapTimerService"/>
/// and <see cref="GeolocationSingleSessionManager"/>, so both produce identical events.
/// </summary>
internal static class SessionEventDetection
{
    /// <summary>
    /// Returns a sector event for the first boundary crossed along the motion
    /// <paramref name="previous"/> → <paramref name="current"/>, or null when no boundary is crossed.
    /// The event timestamp is interpolated at the crossing point; identity fields are left to the caller.
    /// </summary>
    public static SessionDataEvent DetectSectorCrossing(
        CircuitConfiguration circuit,
        GeoTelemetryData previous,
        GeoTelemetryData current)
    {
        var trajectory = new CircuitGeoLine(previous, current);
        var filter = circuit.UseDirection ? CrossingFilter.TowardApex : CrossingFilter.Any;

        // When the motion crosses several boundaries (very low sample rate), keep the earliest one
        CircuitSegment crossed = null;
        var crossedFactor = double.MaxValue;
        foreach (var sector in circuit.Segments)
        {
            var intersect = sector.Boundary.Intersect(trajectory, filter);
            if (intersect == null)
                continue;

            var factor = trajectory.ParameterOf(intersect);
            if (factor < crossedFactor)
            {
                crossed = sector;
                crossedFactor = factor;
            }
        }

        if (crossed == null)
            return null;

        // Linear interpolation of the timestamp at the crossing point
        var dtMs = (current.Timestamp - previous.Timestamp).TotalMilliseconds;
        var adjustedTimestamp = previous.Timestamp.AddMilliseconds(dtMs * crossedFactor);

        // Crossing boundary N completes sector N-1; crossing boundary 1 completes the last sector:
        //   - Closed circuits: last sector is Segments.Count (finish line == start line)
        //   - Open circuits:   finish line is last segment; start line is Segments[0]
        var sectorNumber = crossed.Number == 1 ? circuit.Segments.Count : crossed.Number - 1;

        return new SessionDataEvent
        {
            Timestamp = adjustedTimestamp,
            Sector = sectorNumber,
            Type = SessionEventType.Sector,
            FirstGeoCoordinates = trajectory.Start,
            SecondGeoCoordinates = trajectory.End,
            Factor = crossedFactor
        };
    }

    /// <summary>
    /// Adds the event to <paramref name="session"/>, computing its lap number, per-event time delta
    /// and best flag. If the event completes a lap, a Lap event is derived and added as well.
    /// <paramref name="eventAdded"/> is invoked for every event added.
    /// </summary>
    public static void Register(
        DeviceSessionData session,
        CircuitConfiguration circuit,
        SessionDataEvent sessionEvent,
        Action<SessionDataEvent> eventAdded)
    {
        var lastLap = session.LastLap;
        var lastEvent = session.LastEvent;

        // Lap number: 0 until first pass across start/finish
        sessionEvent.LapNumber = lastLap == null ? 0 : lastLap.LapNumber + 1;

        // Per-event time is the delta since the last event (first event → zero)
        sessionEvent.Time = lastEvent != null
            ? sessionEvent.Timestamp - lastEvent.Timestamp
            : TimeSpan.Zero;

        sessionEvent.DriverRace = session;
        sessionEvent.CircuitCode = circuit.Code;

        if (sessionEvent.Type == SessionEventType.Sector)
            sessionEvent.IsBestOverall = session.IsBestSector(sessionEvent);

        session.AddEvent(sessionEvent);
        eventAdded(sessionEvent);

        // Lap registration rule:
        // - CLOSED: crossing the last sector means the lap has just completed.
        // - OPEN:   crossing the (Count - 1) sector completes the lap (finish line).
        var completesLap = sessionEvent.Type == SessionEventType.Sector &&
            ((circuit.Type == CircuitType.Closed && sessionEvent.Sector == circuit.Segments.Count) ||
             (circuit.Type == CircuitType.Open && sessionEvent.Sector == circuit.Segments.Count - 1));

        if (completesLap)
        {
            TimeSpan lapTime;
            if (lastLap == null)
            {
                lapTime = TimeSpan.Zero; // first time across start line
            }
            else
            {
                // Closed: from last lap timestamp. Open: from last crossing of the start segment
                var reference = circuit.Type == CircuitType.Closed
                    ? lastLap.Timestamp
                    : session.Events
                        .Where(x => x.Sector == circuit.Segments.Count)
                        .Select(x => x.Timestamp)
                        .DefaultIfEmpty(sessionEvent.Timestamp)
                        .Last();

                lapTime = sessionEvent.Timestamp - reference;
            }

            var lapEvent = (SessionDataEvent)sessionEvent.Clone();
            lapEvent.EventId = CompactEventId.NewId();
            lapEvent.Type = SessionEventType.Lap;
            lapEvent.Sector = 0;
            lapEvent.Time = lapTime;
            lapEvent.IsBestOverall = false;

            if (lapTime != TimeSpan.Zero)
            {
                var bestLap = session.BestLap;
                lapEvent.IsBestOverall = bestLap == null || lapTime <= bestLap.Time;
            }

            session.AddEvent(lapEvent);
            eventAdded(lapEvent);
        }

        session.LastPositionTS = sessionEvent.Timestamp;
    }
}
