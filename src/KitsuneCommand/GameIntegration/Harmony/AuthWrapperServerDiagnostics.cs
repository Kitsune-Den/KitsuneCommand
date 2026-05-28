using HarmonyLib;
using KitsuneCommand.Diagnostics;
using LiteNetLib;
using System;
using System.Collections;
using System.Net;
using System.Reflection;

namespace KitsuneCommand.GameIntegration.Harmony
{
    /// <summary>
    /// Diagnostic Harmony patches on the 7DTD server-side LiteNetLib auth
    /// wrapper (<c>NetworkServerLiteNetLib+LiteNetLibAuthWrapperServer</c>).
    ///
    /// 7DTD's challenge-response handshake state machine is normally invisible
    /// at the default INF log level — only the terminal <c>"Peer disconnected
    /// in auth state: {ip} / {reason-int}"</c> shows, leaving operators no way
    /// to tell whether a disconnect was a rate-limit reject, a client
    /// challenge-response timeout, an invalid response, an auth-state Update()
    /// sweep, or something else.
    ///
    /// These patches do TWO things on every relevant event:
    /// <list type="number">
    ///   <item><b>Record</b> a structured <see cref="JoinAttemptEvent"/> into
    ///   the in-memory <see cref="JoinAttemptRing"/>. Always on. The KC web
    ///   panel's "Join Attempts" page reads this ring. Cheap — bounded
    ///   capacity, single lock, no I/O.</item>
    ///   <item><b>Verbose-log</b> the same event to the 7DTD console at INF
    ///   level, tagged <c>[KC-NetDiag]</c>. Gated by <see cref="Enabled"/>
    ///   (default false) because the output is *extremely* chatty. Flip on
    ///   when you want the log file populated too.</item>
    /// </list>
    ///
    /// PURE OBSERVATION — every patch is a Postfix or non-mutating Prefix.
    /// The 500ms connection rate limit, 10s MaxDurationInAuthState, and every
    /// other behavior knob are intentionally left untouched. The goal is to
    /// SEE what's happening, not to change it.
    ///
    /// Why this exists: investigating a "Could not retrieve server
    /// information" failure where the only signal was <c>/ 0</c> for the
    /// reason code (turned out to be `PeerNotFound`, which mapped to a
    /// router-NAT issue). Permanent enough to keep around.
    /// </summary>
    public static class AuthWrapperServerDiagnostics
    {
        /// <summary>
        /// Verbose console logging gate. <see cref="JoinAttemptRing"/>
        /// recording happens regardless — this only controls whether each
        /// event ALSO produces a <c>[KC-NetDiag]</c> line in nssm-stdout.log.
        ///
        /// Flip via reflection from a KC console command, the web panel
        /// (planned), or in code where needed. Default is off because the
        /// log output during a single failed-join burst is ~30 lines in 5
        /// seconds — fine while reproducing a specific bug, exhausting in
        /// steady state.
        /// </summary>
        public static bool Enabled = false;

        // Lazy reflection handle on the wrapper's internal authStates
        // dictionary, used for log-context only (we read .Count, never
        // mutate). Cached at first access to avoid reflection cost per event.
        private static FieldInfo _authStatesField;
        private static FieldInfo AuthStatesField
        {
            get
            {
                if (_authStatesField == null)
                {
                    _authStatesField = AccessTools.Field(
                        typeof(NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer),
                        "authStates");
                }
                return _authStatesField;
            }
        }

        // ConnectionRequest.RemoteEndPoint is an internal field in this
        // LiteNetLib build — not exposed as a public property. Reflection
        // handle so we can record + log who the request came from.
        private static FieldInfo _crRemoteEndPointField;
        private static FieldInfo CrRemoteEndPointField
        {
            get
            {
                if (_crRemoteEndPointField == null)
                {
                    _crRemoteEndPointField = AccessTools.Field(
                        typeof(ConnectionRequest), "RemoteEndPoint");
                }
                return _crRemoteEndPointField;
            }
        }

        // ConnectionRequest.Result is an internal property (and its type
        // ConnectionRequestResult is internal too — so we can't even name
        // it in C#). Read via reflection and ToString() the boxed enum
        // value. The string is what we want for the log + ring anyway —
        // None/Accept/Reject/RejectForce.
        private static PropertyInfo _crResultProp;
        private static PropertyInfo CrResultProp
        {
            get
            {
                if (_crResultProp == null)
                {
                    _crResultProp = AccessTools.Property(
                        typeof(ConnectionRequest), "Result");
                }
                return _crResultProp;
            }
        }

        private static string RequestResult(ConnectionRequest req)
        {
            if (req == null) return null;
            try
            {
                var v = CrResultProp?.GetValue(req);
                return v?.ToString();
            }
            catch { return null; }
        }

        /// <summary>Snapshot of authStates.Count, or null on any failure.</summary>
        private static int? AuthStateCount(
            NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer instance)
        {
            try
            {
                var dict = AuthStatesField?.GetValue(instance) as ICollection;
                return dict?.Count;
            }
            catch
            {
                return null;
            }
        }

        // -------- Endpoint extractors --------
        // Two flavors: one returning IP+port as separate values (for the
        // ring's JoinAttemptEvent which stores them separately), one returning
        // a combined "ip:port" string (for the verbose log line).

        private static (string ip, int? port) PeerIpPort(NetPeer peer)
        {
            if (peer == null) return (null, null);
            try { return (peer.Address?.ToString(), peer.Port); }
            catch { return (null, null); }
        }

        private static (string ip, int? port) RequestIpPort(ConnectionRequest req)
        {
            if (req == null) return (null, null);
            try
            {
                var ep = CrRemoteEndPointField?.GetValue(req) as IPEndPoint;
                if (ep == null) return (null, null);
                return (ep.Address?.ToString(), ep.Port);
            }
            catch { return (null, null); }
        }

        private static string Ep(string ip, int? port)
        {
            if (ip == null) return "(null)";
            return port.HasValue ? (ip + ":" + port.Value) : ip;
        }

        // -------- Patch surfaces --------

        // --- 1. Connection request arrived (pre-handshake) ---

        [HarmonyPatch(
            typeof(NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer),
            nameof(NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer.ConnectionRequestCheck))]
        public static class ConnectionRequestCheckPatch
        {
            [HarmonyPostfix]
            public static void Postfix(
                NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer __instance,
                ConnectionRequest _request)
            {
                // _request.Result is set BY THIS METHOD before we run as
                // postfix — so reading it here tells us whether the wrapper
                // accepted, rejected, or force-rejected. Decisive signal.
                try
                {
                    var (ip, port) = RequestIpPort(_request);
                    var result = RequestResult(_request);
                    var dataBytes = _request?.Data?.AvailableBytes;
                    var authCount = AuthStateCount(__instance);

                    JoinAttemptRing.Record(new JoinAttemptEvent
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "ConnReq",
                        PeerIp = ip,
                        PeerPort = port,
                        Result = result,
                        DataBytes = dataBytes,
                        AuthStateCount = authCount,
                    });

                    if (Enabled)
                    {
                        Log.Out("[KC-NetDiag] ConnReq peer=" + Ep(ip, port)
                            + " result=" + (result ?? "(unknown)")
                            + " dataBytes=" + (dataBytes ?? -1)
                            + " authStateCount=" + (authCount ?? -1));
                    }
                }
                catch (Exception ex)
                {
                    if (Enabled) Log.Warning("[KC-NetDiag] ConnReq Postfix: " + ex.Message);
                }
            }
        }

        // --- 2. Packet received from a peer (challenge response lives here) ---

        [HarmonyPatch(
            typeof(NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer),
            nameof(NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer.OnNetworkReceiveEvent))]
        public static class OnNetworkReceiveEventPatch
        {
            [HarmonyPostfix]
            public static void Postfix(
                NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer __instance,
                NetPeer _peer,
                byte _channel,
                DeliveryMethod _deliveryMethod)
            {
                try
                {
                    var (ip, port) = PeerIpPort(_peer);
                    var authCount = AuthStateCount(__instance);

                    JoinAttemptRing.Record(new JoinAttemptEvent
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "Recv",
                        PeerIp = ip,
                        PeerPort = port,
                        Channel = _channel,
                        DeliveryMethod = _deliveryMethod.ToString(),
                        AuthStateCount = authCount,
                    });

                    if (Enabled)
                    {
                        Log.Out("[KC-NetDiag] Recv peer=" + Ep(ip, port)
                            + " channel=" + _channel
                            + " delivery=" + _deliveryMethod
                            + " authStateCount=" + (authCount ?? -1));
                    }
                }
                catch (Exception ex)
                {
                    if (Enabled) Log.Warning("[KC-NetDiag] Recv Postfix: " + ex.Message);
                }
            }
        }

        // --- 3. Peer officially connected (challenge-response succeeded) ---

        [HarmonyPatch(
            typeof(NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer),
            nameof(NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer.OnPeerConnectedEvent))]
        public static class OnPeerConnectedEventPatch
        {
            [HarmonyPostfix]
            public static void Postfix(
                NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer __instance,
                NetPeer _peer)
            {
                try
                {
                    var (ip, port) = PeerIpPort(_peer);
                    var authCount = AuthStateCount(__instance);

                    JoinAttemptRing.Record(new JoinAttemptEvent
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "Conn",
                        PeerIp = ip,
                        PeerPort = port,
                        AuthStateCount = authCount,
                    });

                    if (Enabled)
                    {
                        Log.Out("[KC-NetDiag] Conn peer=" + Ep(ip, port)
                            + " (challenge passed)"
                            + " authStateCount=" + (authCount ?? -1));
                    }
                }
                catch (Exception ex)
                {
                    if (Enabled) Log.Warning("[KC-NetDiag] Conn Postfix: " + ex.Message);
                }
            }
        }

        // --- 4. Peer disconnect — THE KEY ONE ---
        //
        // Prefix runs before the wrapper's own generic "Peer disconnected in
        // auth state: {0} / {1}" message, so the human-readable reason name
        // appears in the log right above the existing line for correlation.
        // Reasons we expect to see (LiteNetLib's DisconnectReason enum):
        //   ConnectionFailed / Timeout / HostUnreachable / NetworkUnreachable
        //   / RemoteConnectionClose / DisconnectPeerCalled / ConnectionRejected
        //   / InvalidProtocol / UnknownHost / Reconnect / PeerToPeerConnection
        //   / PeerNotFound

        [HarmonyPatch(
            typeof(NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer),
            nameof(NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer.OnPeerDisconnectedEvent))]
        public static class OnPeerDisconnectedEventPatch
        {
            [HarmonyPrefix]
            public static void Prefix(
                NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer __instance,
                NetPeer _peer,
                DisconnectInfo _disconnectInfo)
            {
                try
                {
                    var (ip, port) = PeerIpPort(_peer);
                    var reason = _disconnectInfo.Reason.ToString();
                    var extraBytes = _disconnectInfo.AdditionalData?.AvailableBytes;
                    var authCount = AuthStateCount(__instance);

                    JoinAttemptRing.Record(new JoinAttemptEvent
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "Disc",
                        PeerIp = ip,
                        PeerPort = port,
                        Result = reason,
                        ExtraDataBytes = extraBytes,
                        AuthStateCount = authCount,
                    });

                    if (Enabled)
                    {
                        Log.Out("[KC-NetDiag] Disc peer=" + Ep(ip, port)
                            + " reason=" + reason
                            + " extraDataBytes=" + extraBytes
                            + " authStateCount=" + (authCount ?? -1));
                    }
                }
                catch (Exception ex)
                {
                    if (Enabled) Log.Warning("[KC-NetDiag] Disc Prefix: " + ex.Message);
                }
            }
        }

        // --- 5. Periodic Update — catches auth-state timeout reaps ---
        //
        // Update() runs on a fixed ConnectionStateCheckInterval (10s). The
        // wrapper kills any peer that's been in auth state longer than
        // MaxDurationInAuthState (10s) here, which is a path that does NOT
        // necessarily go through OnPeerDisconnectedEvent. We only log/record
        // when the count changes — otherwise this fires too often to be useful.

        [HarmonyPatch(
            typeof(NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer),
            nameof(NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer.Update))]
        public static class UpdatePatch
        {
            private static int _lastObservedCount;

            [HarmonyPostfix]
            public static void Postfix(
                NetworkServerLiteNetLib.LiteNetLibAuthWrapperServer __instance)
            {
                try
                {
                    var authCount = AuthStateCount(__instance);
                    int n = authCount ?? -1;
                    if (n == _lastObservedCount) return;

                    JoinAttemptRing.Record(new JoinAttemptEvent
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "Update",
                        AuthStateCount = authCount,
                    });

                    if (Enabled)
                    {
                        Log.Out("[KC-NetDiag] Update authStateCount: "
                            + _lastObservedCount + " → " + n);
                    }
                    _lastObservedCount = n;
                }
                catch (Exception ex)
                {
                    if (Enabled) Log.Warning("[KC-NetDiag] Update Postfix: " + ex.Message);
                }
            }
        }
    }
}
