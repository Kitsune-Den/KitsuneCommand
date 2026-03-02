using KitsuneCommand.Configuration;

namespace KitsuneCommand.Core
{
    /// <summary>
    /// Main entry point for the KitsuneCommand mod.
    /// Implements the 7 Days to Die IModApi interface.
    /// </summary>
    public class ModEntry : IModApi
    {
        public static Mod ModInstance { get; private set; }
        public static string ModPath { get; private set; }
        public static SynchronizationContext MainThreadContext { get; private set; }
        public static bool IsGameStartDone { get; internal set; }

        private ModLifecycle _lifecycle;

        public void InitMod(Mod _modInstance)
        {
            ModInstance = _modInstance;
            ModPath = _modInstance.Path;
            MainThreadContext = SynchronizationContext.Current;

            Log.Out("[KitsuneCommand] Initializing KitsuneCommand v2.0.0...");

            _lifecycle = new ModLifecycle();
            _lifecycle.Initialize();

            Log.Out("[KitsuneCommand] Initialization complete. Web panel will be available after game start.");
        }
    }
}
