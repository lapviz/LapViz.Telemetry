using System;
using System.Collections.Generic;

namespace LapViz.Telemetry.Timing.P3;

/// <summary>
/// Framing of the MYLAPS P3 protocol (TranX, ChipX, ProChip, RC4 decoders; TCP port 5403).
/// <para>
/// A record, unescaped: <c>SOR 0x8E</c>, version (1 byte), length of the whole record (2 bytes), CRC (2 bytes),
/// flags (2 bytes), type of record (2 bytes), then fields <c>TOF (1) · LEN (1) · DATA (LEN)</c>, and <c>EOR 0x8F</c>.
/// Numbers are little-endian. Between SOR and EOR, the bytes 0x8D, 0x8E and 0x8F are sent as 0x8D followed by the
/// byte + 0x20. The CRC is CRC-16/CCITT (polynomial 0x1021, initial value 0xFFFF) over the unescaped record with
/// its CRC field set to zero.
/// </para>
/// <para>
/// Written from the public descriptions of the protocol and its open source parsers, without a decoder at hand:
/// to verify with frames captured on a real decoder (RACE-MANAGER.md §11 in LapViz.OpenLiveTiming).
/// </para>
/// </summary>
public static class P3Codec
{
    /// <summary>Start of record.</summary>
    public const byte StartOfRecord = 0x8E;

    /// <summary>End of record.</summary>
    public const byte EndOfRecord = 0x8F;

    /// <summary>Escape byte.</summary>
    public const byte Escape = 0x8D;

    /// <summary>Version written in the header of the records encoded here.</summary>
    public const byte DefaultVersion = 0x02;

    /// <summary>Length of the header, SOR included.</summary>
    public const int HeaderLength = 10;

    private static readonly ushort[] CrcTable = BuildCrcTable();

    /// <summary>CRC-16/CCITT (polynomial 0x1021, initial value 0xFFFF) of <paramref name="count"/> bytes.</summary>
    public static ushort Crc(byte[] data, int offset, int count)
    {
        ushort crc = 0xFFFF;
        for (var i = offset; i < offset + count; i++)
            crc = (ushort)((crc << 8) ^ CrcTable[((crc >> 8) ^ data[i]) & 0xFF]);
        return crc;
    }

    /// <summary>Encodes <paramref name="record"/>: header with its length and CRC, fields, escaping.</summary>
    public static byte[] Encode(P3Record record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));

        var length = HeaderLength + 1;
        foreach (var field in record.Fields)
            length += 2 + field.Data.Length;
        if (length > ushort.MaxValue)
            throw new ArgumentException("The record is too long.", nameof(record));

        var raw = new byte[length];
        raw[0] = StartOfRecord;
        raw[1] = record.Version;
        WriteUInt16(raw, 2, (ushort)length);
        WriteUInt16(raw, 6, record.Flags);
        WriteUInt16(raw, 8, record.Type);
        var i = HeaderLength;
        foreach (var field in record.Fields)
        {
            raw[i++] = field.Type;
            raw[i++] = (byte)field.Data.Length;
            Array.Copy(field.Data, 0, raw, i, field.Data.Length);
            i += field.Data.Length;
        }

        raw[i] = EndOfRecord;
        WriteUInt16(raw, 4, Crc(raw, 0, raw.Length));

        // Escaping of the bytes between SOR and EOR
        var escaped = new List<byte>(raw.Length + 8) { StartOfRecord };
        for (var j = 1; j < raw.Length - 1; j++)
        {
            var b = raw[j];
            if (b >= Escape && b <= EndOfRecord)
            {
                escaped.Add(Escape);
                escaped.Add((byte)(b + 0x20));
            }
            else
            {
                escaped.Add(b);
            }
        }

        escaped.Add(EndOfRecord);
        return escaped.ToArray();
    }

    /// <summary>
    /// Decodes one unescaped record (SOR to EOR included). Null when it is malformed (length, field overrun) or
    /// its CRC is wrong while <paramref name="verifyCrc"/> is set.
    /// </summary>
    public static P3Record? DecodeUnescaped(byte[] raw, bool verifyCrc = true)
    {
        if (raw == null || raw.Length < HeaderLength + 1 || raw[0] != StartOfRecord || raw[raw.Length - 1] != EndOfRecord)
            return null;

        var length = ReadUInt16(raw, 2);
        if (length != raw.Length)
            return null;

        var crc = ReadUInt16(raw, 4);
        var copy = (byte[])raw.Clone();
        copy[4] = 0;
        copy[5] = 0;
        var crcValid = Crc(copy, 0, copy.Length) == crc;
        if (verifyCrc && !crcValid)
            return null;

        var fields = new List<P3Field>();
        var i = HeaderLength;
        while (i < raw.Length - 1)
        {
            if (i + 2 > raw.Length - 1)
                return null;
            var type = raw[i];
            var size = raw[i + 1];
            if (i + 2 + size > raw.Length - 1)
                return null;
            var data = new byte[size];
            Array.Copy(raw, i + 2, data, 0, size);
            fields.Add(new P3Field(type, data));
            i += 2 + size;
        }

        return new P3Record(ReadUInt16(raw, 8), fields, ReadUInt16(raw, 6), raw[1]) { CrcValid = crcValid };
    }

    private static ushort ReadUInt16(byte[] data, int offset) => (ushort)(data[offset] | (data[offset + 1] << 8));

    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }

    private static ushort[] BuildCrcTable()
    {
        var table = new ushort[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (ushort)(i << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
            table[i] = crc;
        }

        return table;
    }
}

/// <summary>
/// Cuts a P3 byte stream (as read from TCP, in chunks of any size) into records: waits for a start of record,
/// unescapes, and drops what does not decode (counted in <see cref="Errors"/>), then resynchronises on the next
/// start of record. Not thread-safe: one reader per connection.
/// </summary>
public sealed class P3StreamDecoder
{
    private readonly List<byte> _record = new List<byte>(64);
    private readonly bool _verifyCrc;
    private bool _inRecord;
    private bool _escaped;

    /// <param name="verifyCrc">Drop records whose CRC does not match (on by default).</param>
    public P3StreamDecoder(bool verifyCrc = true)
    {
        _verifyCrc = verifyCrc;
    }

    /// <summary>Records dropped so far (malformed or wrong CRC), and bytes outside of a record.</summary>
    public int Errors { get; private set; }

    /// <summary>Feeds bytes received; returns the records they complete.</summary>
    public IReadOnlyList<P3Record> Push(byte[] buffer, int offset, int count)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));

        var records = new List<P3Record>();
        for (var i = offset; i < offset + count; i++)
        {
            var b = buffer[i];

            if (b == P3Codec.StartOfRecord)
            {
                if (_inRecord)
                    Errors++; // a record cut short
                _record.Clear();
                _record.Add(b);
                _inRecord = true;
                _escaped = false;
                continue;
            }

            if (!_inRecord)
            {
                Errors++;
                continue;
            }

            if (b == P3Codec.EndOfRecord)
            {
                _record.Add(b);
                _inRecord = false;
                var record = P3Codec.DecodeUnescaped(_record.ToArray(), _verifyCrc);
                if (record != null)
                    records.Add(record);
                else
                    Errors++;
                continue;
            }

            if (_escaped)
            {
                _record.Add((byte)(b - 0x20));
                _escaped = false;
            }
            else if (b == P3Codec.Escape)
            {
                _escaped = true;
            }
            else
            {
                _record.Add(b);
            }
        }

        return records;
    }
}
