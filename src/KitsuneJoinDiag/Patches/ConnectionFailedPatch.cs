using System;
using HarmonyLib;
using LiteNetLib;

namespace KitsuneJoinDiag.Patches
{
    /// <summary>
    /// Postfix on <c>NetworkClientLiteNetLib.OnDisconnectedFromServer</c>
    /// (the client-side handler for LiteNetLib's
    /// <c>NetEventListener.OnPeerDisconnected</c>). When the connection
    /// fails — for any reason, mid-handshake or otherwise — this fires
    /// with the actual <see cref="DisconnectInfo"/>.
    ///
    /// Vanilla 7DTD takes that <see cref="DisconnectInfo"/>, sets a flag
    /// in a closure (<c>NetworkClientLiteNetLib+&lt;&gt;c__DisplayClass13_0</c>
    /// has <c>reason</c>, <c>additionalDisconnectCause</c>,
    /// <c>hasDisconnectInfo</c> fields, captured from this event), and
    /// somewhere downstream populates the UI dialog with a localized
    /// catch-all "Could not retrieve server information" string — the
    /// player never sees the reason.
    ///
    /// We can't easily intercept the dialog text from a source-mode mod
    /// without spelunking through 7DTD's XUiC widget tree, so for v0.1 we
    /// settle for surfacing the reason at the LOG level. Players can read
    /// Player.log after a failed join and see, e.g.:
    ///
    /// <code>
    /// ERR [KitsuneJoinDiag] CONNECTION FAILED — actual LiteNetLib reason:
    ///   reason:           PeerNotFound
    ///   peer:             73.230.2.245:26906
    ///   extraDataBytes:   0
    ///   timeSinceLastPkt: 0.42s
    ///   roundTripTime:    35ms
    /// </code>
    ///
    /// Or paste those lines to an admin and the admin immediately knows
    /// the failure class (NAT/router issue vs version mismatch vs rate
    /// limit vs etc.).
    ///
    /// A future v0.2+ will also patch the XUiC dialog widget to show the
    /// reason in-game; this is the foundation.
    /// </summary>
    [HarmonyPatch(typeof(NetworkClientLiteNetLib), nameof(NetworkClientLiteNetLib.OnDisconnectedFromServer))]
    public static class ClientDisconnectFromServerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NetPeer _peer, DisconnectInfo _info)
        {
            try
            {
                string ep;
                if (_peer == null)
                {
                    ep = "(unknown — peer null at disconnect)";
                }
                else
                {
                    try { ep = _peer.Address + ":" + _peer.Port; }
                    catch { ep = "(peer endpoint read failed)"; }
                }

                string reason = _info.Reason.ToString();
                int extraBytes = _info.AdditionalData != null
                    ? _info.AdditionalData.AvailableBytes
                    : 0;

                // Optional context the player might find useful — the
                // last-packet timing and RTT hint at whether the
                // connection was lively before failing vs DOA.
                string timeSinceLastPkt = "(n/a)";
                string rtt = "(n/a)";
                if (_peer != null)
                {
                    try { timeSinceLastPkt = _peer.TimeSinceLastPacket.ToString("F2") + "s"; } catch { }
                    try { rtt = _peer.RoundTripTime + "ms"; } catch { }
                }

                // ERR level so it's visually obvious in Player.log — the
                // failing player or their admin should be able to spot
                // this block at a glance.
                Log.Error(
                    "\n" +
                    "================================================================\n" +
                    "[KitsuneJoinDiag] CONNECTION FAILED — actual LiteNetLib reason:\n" +
                    "  reason:           " + reason + "\n" +
                    "  peer:             " + ep + "\n" +
                    "  extraDataBytes:   " + extraBytes + "\n" +
                    "  timeSinceLastPkt: " + timeSinceLastPkt + "\n" +
                    "  roundTripTime:    " + rtt + "\n" +
                    HintFor(_info.Reason) +
                    "================================================================");
            }
            catch (Exception ex)
            {
                Log.Warning("[KitsuneJoinDiag] Postfix threw: " + ex.Message);
            }
        }

        /// <summary>
        /// Map of LiteNetLib's <see cref="DisconnectReason"/> values to a
        /// short, player-actionable hint. Conservative wording — we
        /// don't want to mis-diagnose. Anything ambiguous gets a generic
        /// "ask the admin" suggestion rather than confidently wrong
        /// advice.
        /// </summary>
        private static string HintFor(DisconnectReason r)
        {
            switch (r)
            {
                case DisconnectReason.PeerNotFound:
                    return "  hint: server rejected your peer mid-handshake. Common causes:\n" +
                           "        - symmetric NAT on your router rewriting UDP source ports\n" +
                           "        - server-side rate limit (you connected too fast after a previous attempt)\n" +
                           "        - try Direct Connect again in 30 seconds, or use the server's alternate join address\n";

                case DisconnectReason.Timeout:
                    return "  hint: server didn't respond. Check your internet, try a different address,\n" +
                           "        or confirm the server is online with the admin.\n";

                case DisconnectReason.HostUnreachable:
                case DisconnectReason.NetworkUnreachable:
                    return "  hint: no network route to the server. Check your internet connection,\n" +
                           "        VPN status (if any), or the address you typed.\n";

                case DisconnectReason.ConnectionFailed:
                    return "  hint: low-level connection attempt failed (different from timeout).\n" +
                           "        Often a firewall on either side blocking UDP, or a wrong port.\n";

                case DisconnectReason.RemoteConnectionClose:
                    return "  hint: server actively kicked your connection. You may be banned, the server\n" +
                           "        may be full, or your version/mods may not match. Check with the admin.\n";

                case DisconnectReason.ConnectionRejected:
                    return "  hint: server explicitly rejected this connection (vs failing). Common causes:\n" +
                           "        password mismatch, max-player limit, server in protected mode.\n";

                case DisconnectReason.InvalidProtocol:
                    return "  hint: game protocol mismatch. Your client and the server are on different\n" +
                           "        7DTD versions or LiteNetLib versions. Update via Steam.\n";

                case DisconnectReason.UnknownHost:
                    return "  hint: the hostname couldn't be resolved. DNS issue, or you typed the\n" +
                           "        address wrong.\n";

                case DisconnectReason.DisconnectPeerCalled:
                    return "  hint: the server's mod or admin explicitly disconnected you. Check chat\n" +
                           "        history or ask the admin.\n";

                case DisconnectReason.Reconnect:
                    return "  hint: a fresh connection from your IP replaced this one. Probably the game\n" +
                           "        retrying; not actually a fatal failure.\n";

                default:
                    return "  hint: an uncommon LiteNetLib reason — ask the admin to check the server\n" +
                           "        log around this timestamp.\n";
            }
        }
    }
}
