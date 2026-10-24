using System;
using System.Collections.Generic;
using System.Globalization;

namespace LapViz.Telemetry.Timing.P3;

/// <summary>A P3 passing record: a transponder seen by a decoder.</summary>
public sealed class P3Passing
{
    /// <summary>Passing number, counted by the decoder (restarts after a decoder reset).</summary>
    public uint PassingNumber { get; set; }

    /// <summary>Transponder number.</summary>
    public uint Transponder { get; set; }

    /// <summary>Decoder clock time, microseconds since 1970-01-01.</summary>
    public ulong RtcMicroseconds { get; set; }

    /// <summary>UTC time, microseconds since 1970-01-01, when the decoder sends it.</summary>
    public ulong? UtcMicroseconds { get; set; }

    /// <summary>Signal strength.</summary>
    public ushort? Strength { get; set; }

    /// <summary>Number of hits.</summary>
    public ushort? Hits { get; set; }

    /// <summary>Flags of the passing.</summary>
    public ushort? Flags { get; set; }

    /// <summary>Id of the decoder.</summary>
    public uint? DecoderId { get; set; }

    /// <summary>The passing of a record of type <see cref="P3RecordTypes.Passing"/>, null for another record or without transponder.</summary>
    public static P3Passing? FromRecord(P3Record record)
    {
        if (record == null || record.Type != P3RecordTypes.Passing)
            return null;

        var transponder = record.UInt(P3FieldTypes.Transponder);
        var rtc = record.UInt(P3FieldTypes.RtcTime);
        var utc = record.UInt(P3FieldTypes.UtcTime);
        if (!transponder.HasValue || (!rtc.HasValue && !utc.HasValue))
            return null;

        return new P3Passing
        {
            PassingNumber = (uint)(record.UInt(P3FieldTypes.PassingNumber) ?? 0),
            Transponder = (uint)transponder.Value,
            RtcMicroseconds = rtc ?? utc!.Value,
            UtcMicroseconds = utc,
            Strength = (ushort?)record.UInt(P3FieldTypes.Strength),
            Hits = (ushort?)record.UInt(P3FieldTypes.Hits),
            Flags = (ushort?)record.UInt(P3FieldTypes.PassingFlags),
            DecoderId = (uint?)record.UInt(P3FieldTypes.DecoderId),
        };
    }

    /// <summary>The record of this passing (what a decoder sends).</summary>
    public P3Record ToRecord()
    {
        var fields = new List<P3Field>
        {
            P3Field.Of(P3FieldTypes.PassingNumber, PassingNumber, 4),
            P3Field.Of(P3FieldTypes.Transponder, Transponder, 4),
            P3Field.Of(P3FieldTypes.RtcTime, RtcMicroseconds, 8),
        };
        if (Strength.HasValue) fields.Add(P3Field.Of(P3FieldTypes.Strength, Strength.Value, 2));
        if (Hits.HasValue) fields.Add(P3Field.Of(P3FieldTypes.Hits, Hits.Value, 2));
        if (Flags.HasValue) fields.Add(P3Field.Of(P3FieldTypes.PassingFlags, Flags.Value, 2));
        if (UtcMicroseconds.HasValue) fields.Add(P3Field.Of(P3FieldTypes.UtcTime, UtcMicroseconds.Value, 8));
        if (DecoderId.HasValue) fields.Add(P3Field.Of(P3FieldTypes.DecoderId, DecoderId.Value, 4));
        return new P3Record(P3RecordTypes.Passing, fields);
    }

    /// <summary>Microseconds since 1970-01-01 as a time.</summary>
    public static DateTimeOffset TimeOf(ulong microseconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds(0).AddTicks((long)microseconds * 10);

    /// <summary>Time as microseconds since 1970-01-01.</summary>
    public static ulong MicrosecondsOf(DateTimeOffset time) =>
        (ulong)((time.UtcTicks - DateTimeOffset.FromUnixTimeMilliseconds(0).UtcTicks) / 10);
}

/// <summary>A P3 status record: health of a decoder.</summary>
public sealed class P3Status
{
    /// <summary>Noise level.</summary>
    public ushort? Noise { get; set; }

    /// <summary>GPS state.</summary>
    public byte? Gps { get; set; }

    /// <summary>Temperature.</summary>
    public short? Temperature { get; set; }

    /// <summary>Satellites in use.</summary>
    public byte? SatellitesInUse { get; set; }

    /// <summary>Loop triggers.</summary>
    public byte? LoopTriggers { get; set; }

    /// <summary>Input voltage.</summary>
    public byte? InputVoltage { get; set; }

    /// <summary>Id of the decoder.</summary>
    public uint? DecoderId { get; set; }

    /// <summary>The status of a record of type <see cref="P3RecordTypes.Status"/>, null for another record.</summary>
    public static P3Status? FromRecord(P3Record record)
    {
        if (record == null || record.Type != P3RecordTypes.Status)
            return null;

        return new P3Status
        {
            Noise = (ushort?)record.UInt(P3FieldTypes.Noise),
            Gps = (byte?)record.UInt(P3FieldTypes.Gps),
            Temperature = (short?)(ushort?)record.UInt(P3FieldTypes.Temperature),
            SatellitesInUse = (byte?)record.UInt(P3FieldTypes.SatellitesInUse),
            LoopTriggers = (byte?)record.UInt(P3FieldTypes.LoopTriggers),
            InputVoltage = (byte?)record.UInt(P3FieldTypes.InputVoltage),
            DecoderId = (uint?)record.UInt(P3FieldTypes.DecoderId),
        };
    }
}

/// <summary>
/// Turns P3 passings into <see cref="TimingPassing"/>s: the line of the decoder that saw it, the transponder as the
/// competitor key, the time (UTC field when present and preferred, else the decoder clock plus an offset).
/// </summary>
public sealed class P3PassingMapper
{
    private readonly Func<uint?, int?> _lineOfDecoder;

    /// <param name="lineOfDecoder">Timing line of a decoder id (null id: a single decoder); null = passing ignored.</param>
    public P3PassingMapper(Func<uint?, int?> lineOfDecoder)
    {
        _lineOfDecoder = lineOfDecoder ?? throw new ArgumentNullException(nameof(lineOfDecoder));
    }

    /// <summary>Use the UTC time of the passing when the decoder sends one (default true).</summary>
    public bool PreferUtc { get; set; } = true;

    /// <summary>Added to the decoder clock time (time zone of the decoder clock, drift measured by the host).</summary>
    public TimeSpan RtcOffset { get; set; } = TimeSpan.Zero;

    /// <summary>A mapper for one decoder on the finish line.</summary>
    public static P3PassingMapper SingleLoop() => new P3PassingMapper(_ => TimingLineLayout.FinishLine);

    /// <summary>
    /// The timing passing, null when the decoder has no line. Its id joins decoder, passing number and decoder time,
    /// so that two passings never share an id after a decoder reset, and the same passing sent again keeps its id.
    /// </summary>
    public TimingPassing? Map(P3Passing passing)
    {
        if (passing == null) throw new ArgumentNullException(nameof(passing));

        var line = _lineOfDecoder(passing.DecoderId);
        if (!line.HasValue)
            return null;

        var timestamp = PreferUtc && passing.UtcMicroseconds.HasValue
            ? P3Passing.TimeOf(passing.UtcMicroseconds.Value)
            : P3Passing.TimeOf(passing.RtcMicroseconds) + RtcOffset;

        var decoder = passing.DecoderId?.ToString("X8", CultureInfo.InvariantCulture) ?? "0";
        return new TimingPassing
        {
            Id = string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}", decoder, passing.PassingNumber, passing.RtcMicroseconds),
            Key = passing.Transponder.ToString(CultureInfo.InvariantCulture),
            Line = line.Value,
            Timestamp = timestamp,
            Source = decoder,
            Strength = passing.Strength,
            Hits = passing.Hits,
        };
    }
}
