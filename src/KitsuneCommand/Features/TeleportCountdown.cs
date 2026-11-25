namespace KitsuneCommand.Features
{
    /// <summary>Where a player's own teleport is going.</summary>
    public enum TeleportTargetKind
    {
        Home,
        City,
    }

    /// <summary>
    /// A /home or /tp that has passed every check and is waiting out the Teleport delay.
    /// Nothing has been charged or started yet: the city's points and the command's cooldown
    /// are only taken when the teleport actually happens.
    /// </summary>
    public sealed class PendingTeleport
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int EntityId { get; set; }

        public TeleportTargetKind Kind { get; set; }
        /// <summary>The home's or city's name, as saved.</summary>
        public string TargetName { get; set; }
        /// <summary>The destination as saved ("x y z"), for the teleport record.</summary>
        public string TargetPosition { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        /// <summary>The city's points cost when the command was typed; 0 for a home.</summary>
        public int PointsCost { get; set; }

        /// <summary>When the countdown runs out (UTC).</summary>
        public DateTime DueUtc { get; set; }

        /// <summary>Where the player stood when they typed the command.</summary>
        public float StartX { get; set; }
        public float StartY { get; set; }
        public float StartZ { get; set; }
        /// <summary>The player's health the last time it was looked at.</summary>
        public float LastHealth { get; set; }

        /// <summary>How the destination is named in chat: <c>home 'base'</c> or the city name.</summary>
        public string Label => Kind == TeleportTargetKind.Home ? $"home '{TargetName}'" : TargetName;

        /// <summary>The chat command cooldown group this teleport counts against.</summary>
        public string CooldownGroup => Kind == TeleportTargetKind.Home ? "home" : "teleport";

        /// <summary>Same command, same destination: typing it again restarts the countdown.</summary>
        public bool SameTargetAs(PendingTeleport other) =>
            other != null && other.Kind == Kind &&
            string.Equals(other.TargetName, TargetName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A player as the countdown sees them on one tick, read on the main thread.</summary>
    public struct PlayerSnapshot
    {
        public float X;
        public float Y;
        public float Z;
        public float Health;
        public bool IsDead;

        public PlayerSnapshot(float x, float y, float z, float health, bool isDead)
        {
            X = x;
            Y = y;
            Z = z;
            Health = health;
            IsDead = isDead;
        }
    }

    public enum CountdownOutcome
    {
        /// <summary>Keep waiting.</summary>
        Waiting,
        /// <summary>The countdown has run out: teleport now (after the end-of-countdown checks).</summary>
        Due,
        /// <summary>Drop it. <see cref="CountdownStep.Reason"/> says why, or is null when there's nobody to tell.</summary>
        Cancelled,
    }

    public struct CountdownStep
    {
        public CountdownOutcome Outcome;
        /// <summary>For a cancel: what the player did ("you moved"). Null when they've left.</summary>
        public string Reason;

        public static CountdownStep Wait => new CountdownStep { Outcome = CountdownOutcome.Waiting };
        public static CountdownStep Go => new CountdownStep { Outcome = CountdownOutcome.Due };
        public static CountdownStep Cancel(string reason) => new CountdownStep { Outcome = CountdownOutcome.Cancelled, Reason = reason };
    }

    /// <summary>
    /// The Teleport delay for a player's own /home and /tp. No game calls in here: the chat
    /// command service reads the player on the main thread and asks this what to do.
    ///
    /// A pending teleport is cancelled when the player:
    /// <list type="bullet">
    /// <item>moves more than <see cref="MoveToleranceMetres"/> from where they typed it,</item>
    /// <item>loses health (any damage, from anything),</item>
    /// <item>dies,</item>
    /// <item>disconnects (silently: there's nobody to tell),</item>
    /// <item>or asks for another /home or /tp, which replaces it.</item>
    /// </list>
    /// Other chat commands don't touch it.
    /// </summary>
    public static class TeleportCountdown
    {
        /// <summary>The longest delay the panel allows.</summary>
        public const int MaxDelaySeconds = 60;

        /// <summary>
        /// How far a player can drift before the countdown is cancelled, in metres (blocks).
        /// Enough for looking around, a jump in place or being nudged, not enough to walk away.
        /// </summary>
        public const float MoveToleranceMetres = 2f;

        public const string ReasonMoved = "you moved";
        public const string ReasonDamaged = "you took damage";
        public const string ReasonDied = "you died";
        public const string ReasonReplaced = "you asked for another teleport";

        /// <summary>The delay in whole seconds, clamped to 0-60. 0 (or no settings) means instant.</summary>
        public static int DelaySeconds(TeleportSettings settings)
        {
            if (settings == null) return 0;
            return Math.Max(0, Math.Min(MaxDelaySeconds, settings.TeleportDelaySeconds));
        }

        /// <summary>
        /// Starts the countdown on <paramref name="pending"/>: when it's due, and where and how
        /// healthy the player was when they typed the command.
        /// </summary>
        public static void Start(PendingTeleport pending, PlayerSnapshot player, DateTime utcNow, int delaySeconds)
        {
            pending.DueUtc = utcNow.AddSeconds(Math.Max(0, delaySeconds));
            pending.StartX = player.X;
            pending.StartY = player.Y;
            pending.StartZ = player.Z;
            pending.LastHealth = player.Health;
        }

        /// <summary>
        /// What happens to a pending teleport on this tick. <paramref name="player"/> is null when
        /// the player isn't in the world any more. A cancel wins over the countdown running out, so
        /// a hit taken in the last half second still stops it. While waiting, the player's health is
        /// remembered, so healing during the countdown is fine and the next hit still counts.
        /// </summary>
        public static CountdownStep Step(PendingTeleport pending, PlayerSnapshot? player, DateTime utcNow)
        {
            if (player == null) return CountdownStep.Cancel(null);

            var p = player.Value;
            if (p.IsDead) return CountdownStep.Cancel(ReasonDied);
            if (p.Health < pending.LastHealth) return CountdownStep.Cancel(ReasonDamaged);
            if (MovedAway(pending, p)) return CountdownStep.Cancel(ReasonMoved);

            if (utcNow >= pending.DueUtc) return CountdownStep.Go;

            pending.LastHealth = p.Health;
            return CountdownStep.Wait;
        }

        /// <summary>True when the player is more than <see cref="MoveToleranceMetres"/> from where they started.</summary>
        public static bool MovedAway(PendingTeleport pending, PlayerSnapshot player)
        {
            var dx = player.X - pending.StartX;
            var dy = player.Y - pending.StartY;
            var dz = player.Z - pending.StartZ;
            return dx * dx + dy * dy + dz * dz > MoveToleranceMetres * MoveToleranceMetres;
        }

        // ── What the player is told ─────────────────────────────

        public static string StartMessage(PendingTeleport pending, int delaySeconds) =>
            $"Teleporting to {pending.Label} in {delaySeconds}s. Stand still: moving or taking damage cancels it.";

        public static string CancelMessage(PendingTeleport pending, string reason) =>
            $"Teleport to {pending.Label} cancelled: {reason}.";

        /// <summary>
        /// What to tell a player whose pending teleport is replaced by a new one, or null when
        /// they typed the same command again (the new countdown message says it all).
        /// </summary>
        public static string ReplacedMessage(PendingTeleport old, PendingTeleport next) =>
            old == null || old.SameTargetAs(next) ? null : CancelMessage(old, ReasonReplaced);

        /// <summary>The blood moon started while they waited (Teleport settings forbid it).</summary>
        public static string BloodMoonMessage(PendingTeleport pending, string refusal) =>
            $"Teleport to {pending.Label} cancelled. {refusal}";

        /// <summary>They spent the points while they waited. Nothing was charged.</summary>
        public static string NotEnoughPointsMessage(PendingTeleport pending, int have) =>
            $"Teleport to {pending.Label} cancelled: it costs {pending.PointsCost} pts and you have {have}. You were not charged.";
    }
}
