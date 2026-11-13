namespace KitsuneCommand.Core
{
    /// <summary>
    /// Picks how a restart should bring the 7DTD server back for the current
    /// host. KitsuneCommand lives inside the game process, so something
    /// outside it always has to do the starting. Three paths exist:
    ///
    ///   - Linux under systemd: try <c>sudo -n systemctl restart 7daystodie.service</c>
    ///     first (works if <c>scripts/linux-updater/install-linux-updater.sh</c>
    ///     has been run to add the sudoers entry), then fall back to in-game
    ///     shutdown and rely on systemd <c>Restart=always</c>.
    ///   - Windows under a service (NSSM is the usual one): there is no
    ///     <c>systemctl</c>. NSSM ships with <c>AppExit Restart</c> as its
    ///     default, so an in-game shutdown is sufficient. Probing
    ///     <c>systemctl</c> on Windows wastes ~5s on every restart click and
    ///     was the trigger for a cascading crash on a live prod box (the
    ///     fallback shutdown ran concurrent with a stuck main-thread op and
    ///     snowballed into a stack overflow).
    ///   - No supervisor on either OS (server started from
    ///     startdedicated.bat / startserver.sh): leave a helper behind that
    ///     relaunches the server once it has exited. See
    ///     <see cref="ServerRelauncher"/>.
    ///
    /// Kept as a pure function so tests don't need a real OS check or any DI
    /// plumbing.
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

            /// <summary>Nothing supervises the server: arm the relaunch
            /// helper, then shut down in-game.</summary>
            SelfRelaunch,
        }

        /// <summary>
        /// Pick a strategy from the host OS and whether a service supervisor
        /// owns the server process.
        /// </summary>
        public static Kind Decide(bool isWindows, bool isSupervised)
        {
            if (!isSupervised) return Kind.SelfRelaunch;
            return isWindows ? Kind.InGameShutdownOnly : Kind.SystemctlThenInGameShutdown;
        }

        /// <summary>
        /// Convenience: decide based on the running host.
        /// </summary>
        public static Kind DecideForCurrentHost()
        {
            return Decide(PlatformHelper.IsWindows, ServerRelauncher.IsSupervised());
        }
    }
}
