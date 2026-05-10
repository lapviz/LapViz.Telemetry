using System;
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
            SectionTimes = new TimeSpan?[10];
            BestSectionTimes = new TimeSpan?[10];
            IsSectorOverallBest = new bool[10];
            IsSectorPersonalBest = new bool[10];
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

        // ── Identity convenience accessors ────────────────────────
        // These delegate to <see cref="Driver"/> sub-properties so that
        // timing sources can read/write flat names without going through Driver.

        /// <summary>Racing number. Delegates to <see cref="Driver.Number"/>.</summary>
        public string Number { get => Driver.Number ?? ""; set => Driver.Number = value; }

        /// <summary>Full display name. Delegates to <see cref="Driver.Name"/>.</summary>
        public string FullName { get => Driver.Name ?? ""; set => Driver.Name = value; }

        /// <summary>Family / last name. Delegates to <see cref="Driver.LastName"/>.</summary>
        public string LastName { get => Driver.LastName ?? ""; set => Driver.LastName = value; }

        /// <summary>Country / nationality code. Delegates to <see cref="Driver.CountryCode"/>.</summary>
        public string Nationality { get => Driver.CountryCode ?? ""; set => Driver.CountryCode = value; }

        /// <summary>Alias for <see cref="Gap"/>. Used by sources that call the leader gap "Difference".</summary>
        public string Difference { get => Gap ?? ""; set => Gap = value; }

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

        // ── Lap times (snapshot) ──────────────────────────────────

        /// <summary>
        /// Most recent lap time as provided by the timing source.
        /// </summary>
        public TimeSpan? LastTime { get; set; }

        /// <summary>
        /// Personal best lap time in this session.
        /// </summary>
        public TimeSpan? BestTime { get; set; }

        // ── Sector times (snapshot) ───────────────────────────────

        /// <summary>
        /// Current/latest sector times, indexed by sector number.
        /// </summary>
        public TimeSpan?[] SectionTimes { get; set; }

        /// <summary>
        /// Personal best sector times, indexed by sector number.
        /// </summary>
        public TimeSpan?[] BestSectionTimes { get; set; }

        /// <summary>
        /// Per-sector flag: true when the sector time is the session overall best.
        /// Set by sources that provide authoritative best flags.
        /// </summary>
        public bool[] IsSectorOverallBest { get; set; }

        /// <summary>
        /// Per-sector flag: true when the sector time is a personal best.
        /// Set by sources that provide authoritative best flags.
        /// </summary>
        public bool[] IsSectorPersonalBest { get; set; }

        /// <summary>
        /// True when the last lap time is the session overall best.
        /// </summary>
        public bool IsLastLapOverallBest { get; set; }

        /// <summary>
        /// True when the last lap time is a personal best.
        /// </summary>
        public bool IsLastLapPersonalBest { get; set; }

        /// <summary>
        /// Index of the last completed sector (0-based). -1 if none.
        /// </summary>
        public int LastCompletedSector { get; set; } = -1;

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
