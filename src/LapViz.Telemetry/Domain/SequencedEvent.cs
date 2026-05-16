namespace LapViz.Telemetry.Domain;
///
/// <para><b>Purpose (game netcode analogy):</b> In multiplayer games, reliable ordered channels
/// assign each message a sequence number so that receivers can detect gaps and request
/// retransmission. <see cref="SequencedEvent"/> serves the same role — consumers track
/// their last-seen sequence and call <see cref="SessionData.GetEventsSince(long)"/> to
/// catch up on any events they missed.</para>
///


/// <summary>
/// Wraps a <see cref="SessionDataEvent"/> with an instance-local, monotonically increasing
/// sequence number assigned at ingestion time.
///
/// <para><b>Purpose (game netcode analogy):</b> In multiplayer games, reliable ordered channels
/// assign each message a sequence number so that receivers can detect gaps and request
/// retransmission. <see cref="SequencedEvent"/> serves the same role — consumers track
/// their last-seen sequence and call <see cref="SessionData.GetEventsSince(long)"/> to
/// catch up on any events they missed.</para>
///
/// <para><b>Important:</b> Sequence numbers are <em>local</em> to a single
/// <see cref="SessionData"/> instance. They are NOT globally unique across the network.
/// For cross-network deduplication, use <see cref="SessionDataEvent.EventId"/>
/// (<see cref="CompactEventId"/>).</para>
/// </summary>
public class SequencedEvent
{
    /// <summary>
    /// Monotonically increasing sequence number (1-based) assigned when the event
    /// is appended to the local event log. Unique within the owning <see cref="SessionData"/>.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// The underlying telemetry event (immutable reference — do not mutate after ingestion).
    /// </summary>
    public SessionDataEvent Event { get; }

    public SequencedEvent(long sequenceNumber, SessionDataEvent sessionDataEvent)
    {
        SequenceNumber = sequenceNumber;
        Event = sessionDataEvent;
    }
}
