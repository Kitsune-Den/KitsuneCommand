namespace KitsuneCommand.Core
{
    /// <summary>
    /// Picks how the Restart Server endpoint should bounce the 7DTD service for
    /// the current host OS. Two paths exist:
    ///
    ///   - Linux: try <c>sudo -n systemctl restart 7daystodie.service</c> first
    ///     (works if <c>scripts/linux-updater/install-linux-updater.sh</c> has
    ///     been run to add the sudoers entry), then fall back to in-game
    ///     shutdown and rely on systemd <c>Restart=always</c>.
    ///   - Windows: there is no <c>systemctl</c>. NSSM (the standard service
    ///     supervisor for 7DTD on Windows) ships with <c>AppExit Restart</c>
    ///     as its default, so an in-game shutdown is sufficient — NSSM
    ///     auto-bounces the service when the game process exits. Probing
    ///     <c>systemctl</c> on Windows wastes ~5s on every restart click and
    ///     was the trigger for a cascading crash on a live prod box (the
    ///     fallback shutdown ran concurrent with a stuck main-thread op and
    ///     snowballed into a stack overflow).
    ///
    /// Kept as a pure function over <c>isWindows</c> so tests don't need a
    /// real OS check or any DI plumbing.
    /// </summary>
    public static class OsRestartStrategy
    {
        public enum Kind
        {
            /// <summary>Try systemctl first, then fall back to in-game shutdown.</summary>
            SystemctlThenInGameShutdown,

            /// <summary>Skip the systemctl probe entirely; rely on the service
            /// supervisor (NSSM <c>AppExit Restart</c>) to bounce the game
            /// after the in-game shutdown exits the process.</summary>
            InGameShutdownOnly,
        }

        /// <summary>
        /// Pick a strategy based on whether the host is Windows.
        /// </summary>
        public static Kind Decide(bool isWindows)
        {
            return isWindows ? Kind.InGameShutdownOnly : Kind.SystemctlThenInGameShutdown;
        }

        /// <summary>
        /// Convenience: decide based on the running host (uses <see cref="PlatformHelper.IsWindows"/>).
        /// </summary>
        public static Kind DecideForCurrentHost()
        {
            return Decide(PlatformHelper.IsWindows);
        }
    }
}
