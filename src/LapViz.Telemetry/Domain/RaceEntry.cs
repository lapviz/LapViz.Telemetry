using System;

namespace LapViz.Telemetry.Domain
{
    /// <summary>
    /// Represents a single competitor (car/driver) in a race session.
    /// Composes a <see cref="SessionEvents"/> for lap/sector timing with
    /// race-specific state such as position, gap, and status.
    /// </summary>
    public class RaceEntry
    {
        /// <summary>
        /// Creates a new race entry with an empty timing history.
        /// </summary>
        public RaceEntry()
        {
            Timing = new SessionEvents();
            Driver = new Driver();
        }

        /// <summary>
        /// Creates a new race entry for the given driver.
        /// </summary>
        public RaceEntry(Driver driver) : this()
        {
            Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        // ── Identity ──────────────────────────────────────────────

        /// <summary>
        /// The driver associated with this entry.
        /// </summary>
        public Driver Driver { get; set; }

        // ── Timing (composed) ─────────────────────────────────────

        /// <summary>
        /// Lap and sector timing data. All computed properties (BestLap,
        /// BestSectors, Theoretical, Rolling) are available through this object.
        /// </summary>
        public SessionEvents Timing { get; set; }

        // ── Classification ────────────────────────────────────────

        /// <summary>
        /// Current position in the classification (1-based).
        /// </summary>
        public int Position { get; set; }

        /// <summary>
        /// Grid or starting position (1-based).
        /// </summary>
        public int StartPosition { get; set; }

        /// <summary>
        /// Number of completed laps.
        /// </summary>
        public int Laps { get; set; }

        /// <summary>
        /// Total elapsed time since the start (for race classification).
        /// </summary>
        public TimeSpan? TotalTime { get; set; }

        // ── Gaps ──────────────────────────────────────────────────

        /// <summary>
        /// Gap to the leader as a display string (e.g. "+12.345" or "+1 Lap").
        /// </summary>
        public string Gap { get; set; }

        /// <summary>
        /// Interval to the car directly ahead as a display string (e.g. "+1.234").
        /// </summary>
        public string Interval { get; set; }

        // ── Status ────────────────────────────────────────────────

        /// <summary>
        /// Current racing status of the competitor.
        /// </summary>
        public RaceEntryStatus Status { get; set; }

        /// <summary>
        /// True when the competitor has crossed the finish line.
        /// </summary>
        public bool HasFinished { get; set; }

        /// <summary>
        /// Accumulated penalty time in seconds.
        /// </summary>
        public int PenaltyTime { get; set; }

        /// <summary>
        /// Free-form marker or tag attached to this entry by the timing source
        /// (e.g. "PIT", "JUMP START").
        /// </summary>
        public string Marker { get; set; }
    }
}
