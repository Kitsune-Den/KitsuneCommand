using System.Runtime.CompilerServices;

namespace KitsuneCommand.GameIntegration
{
    /// <summary>
    /// Where the 7D2D install lives: the folder with <c>serverconfig.xml</c>,
    /// <c>Data/Worlds</c> and <c>Data/ItemIcons</c>.
    ///
    /// KC used to take this as two folders above its own (<c>Mods/KitsuneCommand/../..</c>).
    /// That only holds when KC is in the install's <c>Mods/</c>. 7D2D also loads mods
    /// from the user-data <c>Mods/</c> folder (e.g. <c>%APPDATA%\7DaysToDie\Mods</c>,
    /// ModManager.ModsBasePath), and installed there KC looked for the world and icons
    /// under the user-data folder and found neither. The game knows its own install
    /// path, so ask it.
    /// </summary>
    public static class GamePaths
    {
        /// <summary>
        /// The install folder, from <c>GameIO.GetApplicationPath()</c> (the folder above
        /// <c>&lt;game&gt;_Data</c>, on every version KC supports). Falls back to two
        /// folders above the mod when the game can't answer, which is also what runs
        /// under unit tests, where the game assembly is a reference-only stub.
        /// </summary>
        public static string InstallDir
        {
            get
            {
                try
                {
                    var fromGame = ApplicationPathFromGame();
                    if (!string.IsNullOrEmpty(fromGame))
                        return Path.GetFullPath(fromGame);
                }
                catch
                {
                    // Not inside the game (tests), or the game isn't initialized yet.
                }

                try { return FromModPath(ModPathFromEntry()); }
                catch { return FromModPath(null); }
            }
        }

        /// <summary>The old guess: two folders above the mod's own.</summary>
        public static string FromModPath(string modPath)
            => Path.GetFullPath(Path.Combine(modPath ?? ".", "..", ".."));

        // Both kept out of line: GameIO and ModEntry each pull in the game assembly,
        // which can't load under tests. Out of line, that failure throws at the call
        // site inside a try instead of when InstallDir itself is compiled.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ApplicationPathFromGame() => GameIO.GetApplicationPath();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ModPathFromEntry() => Core.ModEntry.ModPath;
    }
}
