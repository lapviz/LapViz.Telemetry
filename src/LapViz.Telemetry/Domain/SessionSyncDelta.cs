using System.Collections.Generic;

namespace LapViz.Telemetry.Domain;

/// <summary>
/// Represents the bidirectional delta between two <see cref="SessionData"/> instances,
/// produced by <see cref="SessionData.ComputeDelta"/>.
/// 
/// <para><b>Game netcode analogy — Server Reconciliation:</b></para>
/// <para>In authoritative multiplayer architectures, when two peers (or a client and server)
/// diverge, they exchange their state and each applies the operations they are missing.
/// This class encapsulates exactly that: the two lists of events that need to flow in
/// each direction to achieve eventual consistency.</para>
/// 
/// <para><b>Usage pattern:</b></para>
/// <code>
/// var delta = nodeA.ComputeDelta(nodeB);
/// nodeA.ApplyRemoteEvents(delta.EventsToAddToSource);  // A gets what B has
/// nodeB.ApplyRemoteEvents(delta.EventsToAddToTarget);  // B gets what A has
/// // Both nodes are now consistent.
/// </code>
/// 
/// <para><b>Idempotency:</b> Applying the same delta multiple times is safe because
/// <see cref="SessionData.AddEvent"/> deduplicates by <see cref="CompactEventId"/>.</para>
/// </summary>
public class SessionSyncDelta
{
    /// <summary>
    /// Events present in the source (caller of <see cref="SessionData.ComputeDelta"/>)
    /// but missing in the target (the parameter). Apply these to the target to bring it
    /// up to date with the source.
    /// </summary>
    public IReadOnlyList<SessionDataEvent> EventsToAddToTarget { get; set; }

    /// <summary>
    /// Events present in the target but missing in the source.
    /// Apply these to the source to bring it up to date with the target.
    /// </summary>
    public IReadOnlyList<SessionDataEvent> EventsToAddToSource { get; set; }

    /// <summary>
    /// True if both sides are already in sync (no events to exchange).
    /// </summary>
    public bool IsInSync => (EventsToAddToTarget == null || EventsToAddToTarget.Count == 0)
                         && (EventsToAddToSource == null || EventsToAddToSource.Count == 0);
}
