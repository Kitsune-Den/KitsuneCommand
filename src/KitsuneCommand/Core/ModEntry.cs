using System.Runtime.InteropServices;
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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        private ModLifecycle _lifecycle;

        public void InitMod(Mod _modInstance)
        {
            ModInstance = _modInstance;
            ModPath = _modInstance.Path;
            MainThreadContext = SynchronizationContext.Current;

            // Pre-load native DLLs from x64/ subfolder.
            // Mono's P/Invoke resolver doesn't search PATH like .NET Framework does,
            // so we explicitly load native libraries before any managed code tries to use them.
            var nativePath = Path.Combine(_modInstance.Path, "x64");
            if (Directory.Exists(nativePath))
            {
                PreloadNativeLibrary(nativePath, "SQLite.Interop.dll");
                PreloadNativeLibrary(nativePath, "libSkiaSharp.dll");
            }

            Log.Out("[KitsuneCommand] Initializing KitsuneCommand v2.0.0...");

            _lifecycle = new ModLifecycle();
            _lifecycle.Initialize();

            Log.Out("[KitsuneCommand] Initialization complete. Web panel will be available after game start.");
        }

        private static void PreloadNativeLibrary(string nativePath, string dllName)
        {
            var fullPath = Path.Combine(nativePath, dllName);
            if (File.Exists(fullPath))
            {
                var handle = LoadLibrary(fullPath);
                if (handle != IntPtr.Zero)
                    Log.Out($"[KitsuneCommand] Pre-loaded native library: {dllName}");
                else
                    Log.Warning($"[KitsuneCommand] Failed to pre-load native library: {dllName} (error {Marshal.GetLastWin32Error()})");
            }
        }
    }
}
