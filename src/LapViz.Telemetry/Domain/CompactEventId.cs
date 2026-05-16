using System;
using System.Threading;

namespace LapViz.Telemetry.Domain;
///
/// <para><b>Bit layout (MSB → LSB):</b></para>
/// <code>
/// ┌──────────────────────────────────────────┬──────────────────────┐
/// │  42 bits: ms since 2024-01-01 UTC epoch  │  22 bits: random     │
/// └──────────────────────────────────────────┴──────────────────────┘
/// </code>
///
/// <para><b>Design rationale:</b></para>
/// <list type="bullet">
///   <item><b>8 bytes</b> — 50% smaller than a 16-byte Guid; reduces network payload and
///   hash-table memory in high-throughput telemetry scenarios.</item>
///   <item><b>Time-ordered</b> — IDs are monotonically sortable by creation time, enabling
///   efficient binary search in event logs and B-tree friendly storage.</item>
///   <item><b>~4 million unique IDs per millisecond</b> — 22 random bits provide sufficient
///   entropy for racing telemetry (~tens of events/sec/device across hundreds of nodes).</item>
///   <item><b>No coordination</b> — each node generates independently; no distributed counter
///   or lock needed. Collisions are statistically negligible at expected event rates.</item>
///   <item><b>Embedded timestamp</b> — the creation time can be recovered via
///   <see cref="Timestamp"/> without storing a separate field.</item>
/// </list>
///
/// <para><b>Wire format:</b> <see cref="ToString"/> produces an 11-character Base64url string
/// (no padding, URL-safe). Use <see cref="Parse"/> to reconstruct.</para>
///


/// <summary>
/// A compact 64-bit, time-ordered unique identifier inspired by UUIDv7 (RFC 9562).
///
/// <para><b>Bit layout (MSB → LSB):</b></para>
/// <code>
/// ┌──────────────────────────────────────────┬──────────────────────┐
/// │  42 bits: ms since 2024-01-01 UTC epoch  │  22 bits: random     │
/// └──────────────────────────────────────────┴──────────────────────┘
/// </code>
///
/// <para><b>Design rationale:</b></para>
/// <list type="bullet">
///   <item><b>8 bytes</b> — 50% smaller than a 16-byte Guid; reduces network payload and
///   hash-table memory in high-throughput telemetry scenarios.</item>
///   <item><b>Time-ordered</b> — IDs are monotonically sortable by creation time, enabling
///   efficient binary search in event logs and B-tree friendly storage.</item>
///   <item><b>~4 million unique IDs per millisecond</b> — 22 random bits provide sufficient
///   entropy for racing telemetry (~tens of events/sec/device across hundreds of nodes).</item>
///   <item><b>No coordination</b> — each node generates independently; no distributed counter
///   or lock needed. Collisions are statistically negligible at expected event rates.</item>
///   <item><b>Embedded timestamp</b> — the creation time can be recovered via
///   <see cref="Timestamp"/> without storing a separate field.</item>
/// </list>
///
/// <para><b>Wire format:</b> <see cref="ToString"/> produces an 11-character Base64url string
/// (no padding, URL-safe). Use <see cref="Parse"/> to reconstruct.</para>
///
/// <para><b>Collision probability:</b> For N events in the same millisecond across all nodes,
/// P(collision) ≈ N² / 2^23. At 100 events/ms (extreme), P ≈ 0.0012 — acceptable for
/// telemetry. If exact guarantees are required, combine with a node-specific prefix.</para>
/// </summary>
public readonly struct CompactEventId : IEquatable<CompactEventId>, IComparable<CompactEventId>
{
    // ─── Constants ───────────────────────────────────────────────────────
    // Custom epoch chosen to maximize the usable timestamp range.
    // 42 bits of milliseconds ≈ 139.5 years from this epoch (until ~2163).
    private static readonly DateTimeOffset Epoch = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const int RandomBits = 22;
    private const int RandomMask = (1 << RandomBits) - 1; // 0x3FFFFF

    // ─── Thread-safe random ──────────────────────────────────────────────
    // ThreadStatic avoids contention; each thread gets its own Random instance.
    [ThreadStatic] private static Random _random;
    private static Random Rng => _random ?? (_random = new Random(
        Interlocked.Increment(ref _seed) ^ Environment.TickCount));
    private static int _seed = Environment.TickCount;

    // ─── Instance state ──────────────────────────────────────────────────

    /// <summary>
    /// The raw 64-bit value. Upper 42 bits = milliseconds since epoch, lower 22 bits = random.
    /// This value is suitable for direct use as a dictionary key or database column.
    /// </summary>
    public long Value { get; }

    private CompactEventId(long value) => Value = value;

    // ─── Factory methods ─────────────────────────────────────────────────

    /// <summary>
    /// Generates a new time-ordered compact ID using the current UTC time.
    /// Thread-safe — can be called concurrently from multiple threads without locks.
    /// </summary>
    public static CompactEventId NewId()
    {
        var ms = (long)(DateTimeOffset.UtcNow - Epoch).TotalMilliseconds;
        var rand = Rng.Next(0, 1 << RandomBits);
        var value = (ms << RandomBits) | (long)(rand & RandomMask);
        return new CompactEventId(value);
    }

    /// <summary>
    /// Reconstructs a <see cref="CompactEventId"/> from its raw 64-bit value.
    /// Use this when deserializing from storage or network (JSON long, binary, etc.).
    /// </summary>
    public static CompactEventId FromLong(long value) => new CompactEventId(value);

    // ─── Properties ──────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the approximate UTC creation timestamp embedded in the upper 42 bits.
    /// Precision: millisecond. Useful for diagnostics and time-based filtering without
    /// a separate timestamp field.
    /// </summary>
    public DateTimeOffset Timestamp => Epoch.AddMilliseconds(Value >> RandomBits);

    /// <summary>
    /// The empty/default ID (value 0). Treated as "unset" or "no event".
    /// </summary>
    public static CompactEventId Empty => default;

    /// <summary>
    /// Returns true if this is the default (empty) ID.
    /// </summary>
    public bool IsEmpty => Value == 0;

    // ─── Equality & comparison ───────────────────────────────────────────

    public bool Equals(CompactEventId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is CompactEventId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(CompactEventId other) => Value.CompareTo(other.Value);

    public static bool operator ==(CompactEventId left, CompactEventId right) => left.Value == right.Value;
    public static bool operator !=(CompactEventId left, CompactEventId right) => left.Value != right.Value;

    // ─── Serialization ───────────────────────────────────────────────────

    /// <summary>
    /// Returns an 11-character Base64url string (no padding, URL-safe).
    /// Big-endian byte order ensures lexicographic sort matches numeric sort.
    /// Example: "AYk3Rz_xQAA" → sortable, compact, safe for URLs and JSON.
    /// </summary>
    public override string ToString()
    {
        var bytes = BitConverter.GetBytes(Value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes); // network byte order (big-endian)
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Parses an 11-character Base64url string back into a <see cref="CompactEventId"/>.
    /// Inverse of <see cref="ToString"/>.
    /// </summary>
    /// <exception cref="FormatException">Thrown if the string is not valid Base64url.</exception>
    public static CompactEventId Parse(string s)
    {
        if (string.IsNullOrEmpty(s))
            return Empty;

        // Restore standard Base64 from Base64url encoding
        var padded = s.Replace('-', '+').Replace('_', '/');
        while (padded.Length % 4 != 0) padded += '=';

        var bytes = Convert.FromBase64String(padded);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes); // restore native endianness
        return new CompactEventId(BitConverter.ToInt64(bytes, 0));
    }
}
