using System;

namespace KitsuneCommand.Diagnostics
{
    /// <summary>
    /// A single observed event in a client's connection lifecycle on the
    /// server side, captured by <see cref="GameIntegration.Harmony.AuthWrapperServerDiagnostics"/>
    /// and persisted (in-memory only) by <see cref="JoinAttemptRing"/>.
    ///
    /// One client click of "Direct Connect" in 7DTD typically generates 10-30
    /// of these as LiteNetLib bursts retries and the auth-state state machine
    /// transitions. Operators reading these via the web panel use them to
    /// answer "why did this player just fail to join" — the kind of question
    /// vanilla 7DTD's `Peer disconnected in auth state: ... / 0` log line
    /// flatly refuses to answer.
    ///
    /// Field names are deliberately panel-friendly (not snake_case): this
    /// type is the JSON shape returned by the API and rendered in the Vue
    /// frontend without remapping.
    /// </summary>
    public class JoinAttemptEvent
    {
        /// <summary>UTC timestamp the event was recorded by the patch.</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// One of: ConnReq, Recv, Conn, Disc, Update. Mirrors the patch
        /// surface in AuthWrapperServerDiagnostics.
        /// </summary>
        public string EventType { get; set; }

        /// <summary>Source IP of the peer the event is about. Null for Update events.</summary>
        public string PeerIp { get; set; }

        /// <summary>Source port. Null for Update events.</summary>
        public int? PeerPort { get; set; }

        /// <summary>
        /// For ConnReq: <c>Accept</c> / <c>Reject</c> / <c>RejectForce</c> / <c>None</c>.
        /// For Disc: the LiteNetLib DisconnectReason name (e.g. <c>PeerNotFound</c>,
        /// <c>Timeout</c>, <c>DisconnectPeerCalled</c>).
        /// Null for Conn / Recv / Update.
        /// </summary>
        public string Result { get; set; }

        /// <summary>
        /// For ConnReq: the size of the connect-request payload from the client.
        /// 2 bytes is the LiteNetLib protocol-version handshake; larger sizes mean
        /// the client included extra app-level data.
        /// 0 means the wrapper consumed the bytes during ConnectionRequestCheck
        /// before the diagnostic Postfix ran (so the connect succeeded past the
        /// pre-rate-limit gate).
        /// Null when not applicable.
        /// </summary>
        public int? DataBytes { get; set; }

        /// <summary>Channel byte for Recv events; null otherwise.</summary>
        public int? Channel { get; set; }

        /// <summary>Delivery method for Recv events (ReliableOrdered, Unreliable, etc.); null otherwise.</summary>
        public string DeliveryMethod { get; set; }

        /// <summary>Size of the disconnect packet's optional payload, for Disc events.</summary>
        public int? ExtraDataBytes { get; set; }

        /// <summary>
        /// authStates dict size AT THE TIME OF THIS EVENT, snapshotted via reflection
        /// from the wrapper instance. Useful for spotting bursts (multiple peers
        /// in auth state simultaneously) and stuck connections (count stays &gt; 0).
        /// </summary>
        public int? AuthStateCount { get; set; }
    }
}
