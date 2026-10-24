using System;
using System.Collections.Generic;

namespace LapViz.Telemetry.Timing.P3;

/// <summary>Types of record (TOR) of the MYLAPS P3 protocol used here.</summary>
public static class P3RecordTypes
{
    /// <summary>A transponder passing.</summary>
    public const ushort Passing = 0x0001;

    /// <summary>Decoder status (noise, GPS, temperature, loop).</summary>
    public const ushort Status = 0x0002;

    /// <summary>Decoder version.</summary>
    public const ushort Version = 0x0003;

    /// <summary>Request to send the passings of a range of passing numbers again.</summary>
    public const ushort Resend = 0x0004;
}

/// <summary>Types of field (TOF) of the MYLAPS P3 protocol used here.</summary>
public static class P3FieldTypes
{
    /// <summary>Passing: passing number (uint32), counted by the decoder.</summary>
    public const byte PassingNumber = 0x01;

    /// <summary>Passing: transponder number (uint32).</summary>
    public const byte Transponder = 0x03;

    /// <summary>Passing: decoder clock time, microseconds since 1970-01-01 (uint64).</summary>
    public const byte RtcTime = 0x04;

    /// <summary>Passing: signal strength (uint16).</summary>
    public const byte Strength = 0x05;

    /// <summary>Passing: number of hits (uint16).</summary>
    public const byte Hits = 0x06;

    /// <summary>Passing: flags (uint16).</summary>
    public const byte PassingFlags = 0x08;

    /// <summary>Passing: UTC time, microseconds since 1970-01-01 (uint64), on decoders that know it.</summary>
    public const byte UtcTime = 0x10;

    /// <summary>Status: noise level (uint16).</summary>
    public const byte Noise = 0x01;

    /// <summary>Status: GPS state (uint8).</summary>
    public const byte Gps = 0x06;

    /// <summary>Status: temperature (int16).</summary>
    public const byte Temperature = 0x07;

    /// <summary>Status: satellites in use (uint8).</summary>
    public const byte SatellitesInUse = 0x0A;

    /// <summary>Status: loop triggers (uint8).</summary>
    public const byte LoopTriggers = 0x0B;

    /// <summary>Status: input voltage (uint8).</summary>
    public const byte InputVoltage = 0x0C;

    /// <summary>Resend: first passing number (uint32).</summary>
    public const byte ResendFrom = 0x01;

    /// <summary>Resend: last passing number (uint32).</summary>
    public const byte ResendUntil = 0x02;

    /// <summary>Any record: id of the decoder (uint32).</summary>
    public const byte DecoderId = 0x81;
}

/// <summary>A field of a P3 record: type (TOF) and little-endian data.</summary>
public sealed class P3Field
{
    /// <summary>Creates a field.</summary>
    public P3Field(byte type, byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (data.Length > byte.MaxValue) throw new ArgumentException("A field holds at most 255 bytes.", nameof(data));
        Type = type;
        Data = data;
    }

    /// <summary>Type of field (TOF).</summary>
    public byte Type { get; }

    /// <summary>Raw data, little-endian for numbers.</summary>
    public byte[] Data { get; }

    /// <summary>The data as an unsigned little-endian integer (at most 8 bytes).</summary>
    public ulong AsUInt64()
    {
        ulong value = 0;
        for (var i = Math.Min(Data.Length, 8) - 1; i >= 0; i--)
            value = (value << 8) | Data[i];
        return value;
    }

    /// <summary>A field holding <paramref name="value"/> on <paramref name="size"/> little-endian bytes.</summary>
    public static P3Field Of(byte type, ulong value, int size)
    {
        var data = new byte[size];
        for (var i = 0; i < size; i++)
            data[i] = (byte)(value >> (8 * i));
        return new P3Field(type, data);
    }
}

/// <summary>A record of the MYLAPS P3 protocol, unescaped. Unknown types and fields are kept as they are.</summary>
public sealed class P3Record
{
    /// <summary>Creates a record.</summary>
    public P3Record(ushort type, IReadOnlyList<P3Field> fields, ushort flags = 0, byte version = P3Codec.DefaultVersion)
    {
        Type = type;
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        Flags = flags;
        Version = version;
    }

    /// <summary>Type of record (TOR), see <see cref="P3RecordTypes"/>.</summary>
    public ushort Type { get; }

    /// <summary>Fields in their order.</summary>
    public IReadOnlyList<P3Field> Fields { get; }

    /// <summary>Flags of the header.</summary>
    public ushort Flags { get; }

    /// <summary>Protocol version of the header.</summary>
    public byte Version { get; }

    /// <summary>False when the received CRC did not match (only kept when the check is off).</summary>
    public bool CrcValid { get; internal set; } = true;

    /// <summary>The first field of type <paramref name="type"/>, null when absent.</summary>
    public P3Field? Field(byte type)
    {
        foreach (var field in Fields)
        {
            if (field.Type == type)
                return field;
        }

        return null;
    }

    /// <summary>The value of the field <paramref name="type"/> as an unsigned integer, null when absent.</summary>
    public ulong? UInt(byte type) => Field(type)?.AsUInt64();
}
