using System.Collections.Generic;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// One step of the warning ladder that plays out before a graceful restart.
    /// Persisted as part of <see cref="GracefulRestartSettings"/>.
    /// </summary>
    public class RestartWarning
    {
        /// <summary>
        /// How many whole minutes before the scheduled restart this warning fires.
        /// 0 means the final "shutting down now" message that runs ~10 seconds
        /// before the actual shutdown command.
        /// </summary>
        public int MinutesBefore { get; set; }

        /// <summary>
        /// Broadcast text. Tokens substituted at fire time:
        ///   {minutes}  → the MinutesBefore value (e.g. "10", "5", "1")
        /// Plain in-game color hex codes in [BBGGRR] form work too — 7D2D's
        /// say command renders them inline in chat.
        /// </summary>
        public string Message { get; set; }

        /// <summary>BBGGRR hex (no #, no alpha). Wraps the rendered Message.</summary>
        public string ColorHex { get; set; } = "FFFFFF";
    }

    /// <summary>
    /// Configuration for the graceful-restart feature. Persisted as a JSON blob
    /// in the settings table under key "GracefulRestart" (same pattern as the
    /// other Feature modules).
    /// </summary>
    public class GracefulRestartSettings
    {
        /// <summary>Master toggle. When off, no scheduled restarts fire.</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Time of day for the daily restart, in HH:mm (24h). Interpreted in the
        /// timezone defined by <see cref="ScheduledTimezone"/> so DST shifts
        /// don't drift the wall-clock anchor.
        /// </summary>
        public string ScheduledTime { get; set; } = "04:00";

        /// <summary>
        /// IANA timezone name for the schedule. Defaults to America/Los_Angeles
        /// because that's where many of our admins live and "4am PT" is the
        /// canonical low-traffic restart slot.
        /// </summary>
        public string ScheduledTimezone { get; set; } = "America/Los_Angeles";

        /// <summary>
        /// Warning ladder, ordered by time. Defaults give players a 10-minute
        /// wind-down with escalating tone. Admins can rewrite messages or add/
        /// remove steps via the panel.
        /// </summary>
        public List<RestartWarning> WarningLadder { get; set; } = new List<RestartWarning>
        {
            new RestartWarning { MinutesBefore = 10, Message = "Server restarting in {minutes} minutes. Please save and disconnect when ready.", ColorHex = "FFAA00" },
            new RestartWarning { MinutesBefore = 5,  Message = "Server restarting in {minutes} minutes.",                                         ColorHex = "FFAA00" },
            new RestartWarning { MinutesBefore = 1,  Message = "Server restarting in {minutes} minute. Disconnect now to avoid data loss.",      ColorHex = "FF6600" },
            new RestartWarning { MinutesBefore = 0,  Message = "Restarting now...",                                                                ColorHex = "FF0000" },
        };
    }
}
