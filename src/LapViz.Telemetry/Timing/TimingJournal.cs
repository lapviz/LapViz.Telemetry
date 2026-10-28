using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LapViz.Telemetry.Domain;

namespace LapViz.Telemetry.Timing;

/// <summary>Kinds of entries of a <see cref="TimingJournal"/>.</summary>
public enum TimingJournalEntryKind
{
    /// <summary>Header: layout and rules of the session (first entry).</summary>
    Session,

    /// <summary>A passing.</summary>
    Passing,

    /// <summary>Start of the session.</summary>
    Start,

    /// <summary>Flag change.</summary>
    Flag,

    /// <summary>Checkered flag shown by hand.</summary>
    Checkered,

    /// <summary>Passing invalidated by the operator.</summary>
    Invalidate,

    /// <summary>Invalidated passing counted again.</summary>
    Restore,

    /// <summary>Competitor registered or changed.</summary>
    Competitor,

    /// <summary>Penalties of a competitor.</summary>
    Penalty,

    /// <summary>Status of a competitor set or cleared.</summary>
    Status
}

/// <summary>One line of a <see cref="TimingJournal"/>. Only the members of its kind are set.</summary>
public sealed class TimingJournalEntry
{
    /// <summary>Kind of entry.</summary>
    [JsonPropertyName("k")]
    public TimingJournalEntryKind Kind { get; set; }

    /// <summary>Time of the passing or of the command (decoder time).</summary>
    [JsonPropertyName("at")]
    public DateTimeOffset? At { get; set; }

    /// <summary>Passing id (passing, invalidate, restore).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Competitor key (passing, competitor, penalty, status).</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>Line of a passing.</summary>
    [JsonPropertyName("line")]
    public int? Line { get; set; }

    /// <summary>Source (decoder) of a passing.</summary>
    [JsonPropertyName("src")]
    public string? Source { get; set; }

    /// <summary>Signal strength of a passing.</summary>
    [JsonPropertyName("str")]
    public int? Strength { get; set; }

    /// <summary>Hits of a passing.</summary>
    [JsonPropertyName("hits")]
    public int? Hits { get; set; }

    /// <summary>Passing added by the operator.</summary>
    [JsonPropertyName("man")]
    public bool? Manual { get; set; }

    /// <summary>Flag of a flag change.</summary>
    [JsonPropertyName("flag")]
    public RaceFlag? Flag { get; set; }

    /// <summary>Racing number of a competitor.</summary>
    [JsonPropertyName("num")]
    public string? Number { get; set; }

    /// <summary>Name of a competitor.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Class of a competitor.</summary>
    [JsonPropertyName("class")]
    public string? ClassName { get; set; }

    /// <summary>Grid position of a competitor.</summary>
    [JsonPropertyName("grid")]
    public int? StartPosition { get; set; }

    /// <summary>Time penalty in seconds.</summary>
    [JsonPropertyName("pen")]
    public double? PenaltySeconds { get; set; }

    /// <summary>Penalty laps.</summary>
    [JsonPropertyName("penLaps")]
    public int? PenaltyLaps { get; set; }

    /// <summary>Status of a competitor (null: cleared).</summary>
    [JsonPropertyName("status")]
    public RaceEntryStatus? Status { get; set; }

    /// <summary>Session: lines on the track.</summary>
    [JsonPropertyName("lines")]
    public int? SectorLines { get; set; }

    /// <summary>Session: pit in line.</summary>
    [JsonPropertyName("pitIn")]
    public int? PitInLine { get; set; }

    /// <summary>Session: pit out line.</summary>
    [JsonPropertyName("pitOut")]
    public int? PitOutLine { get; set; }

    /// <summary>Session: run type.</summary>
    [JsonPropertyName("run")]
    public RaceRunType? RunType { get; set; }

    /// <summary>Session: classification.</summary>
    [JsonPropertyName("cls")]
    public TimingClassificationMode? Classification { get; set; }

    /// <summary>Session: start mode.</summary>
    [JsonPropertyName("startMode")]
    public TimingStartMode? StartMode { get; set; }

    /// <summary>Session: duration in seconds.</summary>
    [JsonPropertyName("dur")]
    public double? DurationSeconds { get; set; }

    /// <summary>Session: laps of a race.</summary>
    [JsonPropertyName("laps")]
    public int? Laps { get; set; }

    /// <summary>Session: minimum lap time in seconds.</summary>
    [JsonPropertyName("minLap")]
    public double? MinLapSeconds { get; set; }

    /// <summary>Session: maximum lap time in seconds (absent = none).</summary>
    [JsonPropertyName("maxLap")]
    public double? MaxLapSeconds { get; set; }

    /// <summary>Session: minimum pit stop time in seconds (absent = none).</summary>
    [JsonPropertyName("minPit")]
    public double? MinPitStopSeconds { get; set; }

    /// <summary>Session: false when the red flag does not stop the clock (absent = it stops it).</summary>
    [JsonPropertyName("redStopsClock")]
    public bool? RedFlagStopsClock { get; set; }

    /// <summary>Session: minimum time between two passings on a line, in seconds.</summary>
    [JsonPropertyName("minPass")]
    public double? MinPassingSeconds { get; set; }
}

/// <summary>
/// The journal of a <see cref="PassingTimingSession"/>: one JSON object per line (NDJSON), a session header then
/// every passing and command in the order received. The host appends each entry before applying it; replaying the
/// file rebuilds the same session after a crash or a restart, and the file is the audit of the session.
/// Unknown kinds and unreadable lines are skipped, so later versions can add entries.
/// </summary>
public static class TimingJournal
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>One line of JSON (no line break).</summary>
    public static string Serialize(TimingJournalEntry entry) => JsonSerializer.Serialize(entry, Options);

    /// <summary>The entry of a line, null when the line is empty or unreadable.</summary>
    public static TimingJournalEntry? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            return JsonSerializer.Deserialize<TimingJournalEntry>(line!, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The header of a session.</summary>
    public static TimingJournalEntry Session(TimingLineLayout layout, TimingRules rules)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        if (rules == null) throw new ArgumentNullException(nameof(rules));

        return new TimingJournalEntry
        {
            Kind = TimingJournalEntryKind.Session,
            SectorLines = layout.SectorLines,
            PitInLine = layout.PitInLine,
            PitOutLine = layout.PitOutLine,
            RunType = rules.RunType,
            Classification = rules.Classification,
            StartMode = rules.StartMode,
            DurationSeconds = rules.Duration?.TotalSeconds,
            Laps = rules.Laps,
            MinLapSeconds = rules.MinLapTime.TotalSeconds,
            MinPassingSeconds = rules.MinPassingInterval.TotalSeconds,
            MaxLapSeconds = rules.MaxLapTime > TimeSpan.Zero ? rules.MaxLapTime.TotalSeconds : null,
            MinPitStopSeconds = rules.MinPitStopTime > TimeSpan.Zero ? rules.MinPitStopTime.TotalSeconds : null,
            RedFlagStopsClock = rules.RedFlagStopsClock ? null : false,
        };
    }

    /// <summary>A passing.</summary>
    public static TimingJournalEntry Passing(TimingPassing passing) => new TimingJournalEntry
    {
        Kind = TimingJournalEntryKind.Passing,
        Id = passing.Id,
        Key = passing.Key,
        Line = passing.Line,
        At = passing.Timestamp,
        Source = passing.Source,
        Strength = passing.Strength,
        Hits = passing.Hits,
        Manual = passing.Manual ? true : null,
    };

    /// <summary>Start of the session.</summary>
    public static TimingJournalEntry Start(DateTimeOffset at) => new TimingJournalEntry { Kind = TimingJournalEntryKind.Start, At = at };

    /// <summary>Flag change.</summary>
    public static TimingJournalEntry Flag(RaceFlag flag, DateTimeOffset at) => new TimingJournalEntry { Kind = TimingJournalEntryKind.Flag, Flag = flag, At = at };

    /// <summary>Checkered flag shown by hand.</summary>
    public static TimingJournalEntry Checkered(DateTimeOffset at) => new TimingJournalEntry { Kind = TimingJournalEntryKind.Checkered, At = at };

    /// <summary>Passing invalidated.</summary>
    public static TimingJournalEntry Invalidate(string passingId) => new TimingJournalEntry { Kind = TimingJournalEntryKind.Invalidate, Id = passingId };

    /// <summary>Invalidated passing counted again.</summary>
    public static TimingJournalEntry Restore(string passingId) => new TimingJournalEntry { Kind = TimingJournalEntryKind.Restore, Id = passingId };

    /// <summary>Competitor registered or changed.</summary>
    public static TimingJournalEntry Competitor(TimingCompetitor competitor) => new TimingJournalEntry
    {
        Kind = TimingJournalEntryKind.Competitor,
        Key = competitor.Key,
        Number = competitor.Number,
        Name = competitor.Name,
        ClassName = competitor.ClassName,
        StartPosition = competitor.StartPosition,
    };

    /// <summary>Penalties of a competitor.</summary>
    public static TimingJournalEntry Penalty(string key, TimeSpan time, int laps = 0) => new TimingJournalEntry
    {
        Kind = TimingJournalEntryKind.Penalty,
        Key = key,
        PenaltySeconds = time.TotalSeconds,
        PenaltyLaps = laps,
    };

    /// <summary>Status of a competitor set (or cleared with null).</summary>
    public static TimingJournalEntry Status(string key, RaceEntryStatus? status) => new TimingJournalEntry
    {
        Kind = TimingJournalEntryKind.Status,
        Key = key,
        Status = status,
    };

    /// <summary>A new session from a header entry.</summary>
    public static PassingTimingSession CreateSession(TimingJournalEntry header)
    {
        if (header == null) throw new ArgumentNullException(nameof(header));
        if (header.Kind != TimingJournalEntryKind.Session) throw new ArgumentException("Not a session header.", nameof(header));

        var rules = new TimingRules
        {
            RunType = header.RunType ?? RaceRunType.Practice,
            Classification = header.Classification ?? TimingClassificationMode.BestLap,
            StartMode = header.StartMode ?? TimingStartMode.FirstCrossing,
            Duration = header.DurationSeconds.HasValue ? TimeSpan.FromSeconds(header.DurationSeconds.Value) : null,
            Laps = header.Laps ?? 0,
        };
        if (header.MinLapSeconds.HasValue)
            rules.MinLapTime = TimeSpan.FromSeconds(header.MinLapSeconds.Value);
        if (header.MinPassingSeconds.HasValue)
            rules.MinPassingInterval = TimeSpan.FromSeconds(header.MinPassingSeconds.Value);
        if (header.MaxLapSeconds.HasValue)
            rules.MaxLapTime = TimeSpan.FromSeconds(header.MaxLapSeconds.Value);
        if (header.MinPitStopSeconds.HasValue)
            rules.MinPitStopTime = TimeSpan.FromSeconds(header.MinPitStopSeconds.Value);
        rules.RedFlagStopsClock = header.RedFlagStopsClock ?? true;

        return new PassingTimingSession(new TimingLineLayout(header.SectorLines ?? 1, header.PitInLine, header.PitOutLine), rules);
    }

    /// <summary>Applies an entry to <paramref name="session"/>. False when it is incomplete, a header or unknown.</summary>
    public static bool Apply(TimingJournalEntry entry, PassingTimingSession session)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        if (session == null) throw new ArgumentNullException(nameof(session));

        switch (entry.Kind)
        {
            case TimingJournalEntryKind.Passing when entry.Id != null && entry.Key != null && entry.At.HasValue:
                session.AddPassing(new TimingPassing
                {
                    Id = entry.Id,
                    Key = entry.Key,
                    Line = entry.Line ?? TimingLineLayout.FinishLine,
                    Timestamp = entry.At.Value,
                    Source = entry.Source,
                    Strength = entry.Strength,
                    Hits = entry.Hits,
                    Manual = entry.Manual == true,
                });
                return true;

            case TimingJournalEntryKind.Start when entry.At.HasValue:
                session.Start(entry.At.Value);
                return true;

            case TimingJournalEntryKind.Flag when entry.At.HasValue && entry.Flag.HasValue:
                session.SetFlag(entry.Flag.Value, entry.At.Value);
                return true;

            case TimingJournalEntryKind.Checkered when entry.At.HasValue:
                session.Checkered(entry.At.Value);
                return true;

            case TimingJournalEntryKind.Invalidate when entry.Id != null:
                session.InvalidatePassing(entry.Id);
                return true;

            case TimingJournalEntryKind.Restore when entry.Id != null:
                session.RestorePassing(entry.Id);
                return true;

            case TimingJournalEntryKind.Competitor when !string.IsNullOrWhiteSpace(entry.Key):
                session.SetCompetitor(new TimingCompetitor
                {
                    Key = entry.Key!,
                    Number = entry.Number ?? "",
                    Name = entry.Name ?? "",
                    ClassName = entry.ClassName ?? "",
                    StartPosition = entry.StartPosition ?? 0,
                });
                return true;

            case TimingJournalEntryKind.Penalty when !string.IsNullOrWhiteSpace(entry.Key):
                session.SetPenalty(entry.Key!, TimeSpan.FromSeconds(entry.PenaltySeconds ?? 0), entry.PenaltyLaps ?? 0);
                return true;

            case TimingJournalEntryKind.Status when !string.IsNullOrWhiteSpace(entry.Key):
                session.SetStatus(entry.Key!, entry.Status);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Rebuilds the session of a journal: its first readable entry must be the session header (else null).
    /// <paramref name="skipped"/> counts the lines not applied (unreadable, unknown, incomplete).
    /// </summary>
    public static PassingTimingSession? Replay(TextReader reader, out int skipped)
    {
        if (reader == null) throw new ArgumentNullException(nameof(reader));

        skipped = 0;
        PassingTimingSession? session = null;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var entry = Parse(line);
            if (entry == null)
            {
                skipped++;
                continue;
            }

            if (session == null)
            {
                if (entry.Kind != TimingJournalEntryKind.Session)
                    return null;
                session = CreateSession(entry);
                continue;
            }

            if (!Apply(entry, session))
                skipped++;
        }

        return session;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
