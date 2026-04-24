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

        /// <summary>
        /// The mod version read from ModInfo.xml at startup. Single source of truth —
        /// surfaces to the /api/server/info endpoint and to startup log lines so we
        /// never have to hand-bump version strings in C# + XML + TS in lockstep.
        /// Falls back to "unknown" if ModInfo.xml can't be read.
        /// </summary>
        public static string ModVersion { get; private set; } = "unknown";

        // Windows native library loading
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        // Linux native library loading (Mono)
        [DllImport("libdl.so.2", EntryPoint = "dlopen")]
        private static extern IntPtr dlopen(string filename, int flags);
        private const int RTLD_NOW = 2;
        private const int RTLD_GLOBAL = 0x100;

        // Mono runtime DLL mapping — register native library redirects at runtime
        // so Mono can find our native libraries by full path during P/Invoke resolution.
        [DllImport("__Internal", EntryPoint = "mono_dllmap_insert")]
        private static extern void mono_dllmap_insert(
            IntPtr assembly, string dll, string func, string tdll, string tfunc);

        private ModLifecycle _lifecycle;

        public void InitMod(Mod _modInstance)
        {
            ModInstance = _modInstance;
            ModPath = _modInstance.Path;
            MainThreadContext = SynchronizationContext.Current;

            // Parse ModInfo.xml once at startup so ModVersion is available to any
            // consumer (web API, log lines, etc.) without re-reading the file.
            try
            {
                var modInfoPath = Path.Combine(ModPath, "ModInfo.xml");
                if (File.Exists(modInfoPath))
                {
                    var doc = System.Xml.Linq.XDocument.Load(modInfoPath);
                    var versionAttr = doc.Root?.Element("Version")?.Attribute("value")?.Value;
                    if (!string.IsNullOrWhiteSpace(versionAttr))
                        ModVersion = versionAttr;
                }
            }
            catch
            {
                // Keep "unknown" — not worth blocking init over a metadata read.
            }

            // Pre-load native libraries from the platform-specific subfolder.
            // Mono's P/Invoke resolver doesn't search mod directories by default,
            // so we register runtime DLL maps and pre-load libraries explicitly.
            //
            // System.Data.SQLite is built from source with SQLITE_STANDARD, so its
            // P/Invoke target is "sqlite3" (not "SQLite.Interop.dll"). On Linux,
            // Mono's global config maps sqlite3 -> libsqlite3.so.0 automatically.
            // On Windows, we ship the official sqlite3.dll from sqlite.org.
            if (PlatformHelper.IsLinux)
            {
                // Native libs live in Mods/KitsuneCommand/x64/ on BOTH Windows and
                // Linux — same folder, different file extensions. SkiaSharp's own
                // LibraryLoader looks at {assemblyDir}/x64/libSkiaSharp.so (when
                // PlatformConfiguration.LinuxFlavor is null, which it is on glibc),
                // so shipping there is the path of least resistance.
                var nativePath = Path.Combine(_modInstance.Path, "x64");
                if (Directory.Exists(nativePath))
                {
                    // SkiaSharp needs explicit DLL map + preload since Mono won't
                    // find it in the mod's subfolder otherwise.
                    RegisterMonoDllMap("libSkiaSharp",
                        Path.Combine(nativePath, "libSkiaSharp.so"));
                    PreloadNativeLibrary(nativePath, "libSkiaSharp.so");
                }

                // SQLite: no action needed. Mono's global config already maps
                // "sqlite3" -> "libsqlite3.so.0" (system package libsqlite3-0).
            }
            else
            {
                var nativePath = Path.Combine(_modInstance.Path, "x64");
                if (Directory.Exists(nativePath))
                {
                    // Register DLL map so Mono resolves "sqlite3" -> our sqlite3.dll
                    // in the mod's x64/ subfolder (Mono won't search there by default).
                    RegisterMonoDllMap("sqlite3",
                        Path.Combine(nativePath, "sqlite3.dll"));
                    PreloadNativeLibrary(nativePath, "sqlite3.dll");
                    PreloadNativeLibrary(nativePath, "libSkiaSharp.dll");
                }
            }

            Log.Out($"[KitsuneCommand] Initializing KitsuneCommand v{ModVersion} on {(PlatformHelper.IsLinux ? "Linux" : "Windows")}...");

            _lifecycle = new ModLifecycle();
            _lifecycle.Initialize();

            Log.Out("[KitsuneCommand] Initialization complete. Web panel will be available after game start.");
        }

        private static void RegisterMonoDllMap(string dllName, string targetPath)
        {
            try
            {
                // IntPtr.Zero = global mapping (applies to all assemblies)
                // null func/tfunc = map all functions (not just specific ones)
                mono_dllmap_insert(IntPtr.Zero, dllName, null, targetPath, null);
                Log.Out($"[KitsuneCommand] Registered DLL map: {dllName} -> {targetPath}");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to register DLL map for {dllName}: {ex.Message}");
            }
        }

        private static void PreloadNativeLibrary(string nativePath, string libName)
        {
            var fullPath = Path.Combine(nativePath, libName);
            if (!File.Exists(fullPath))
            {
                Log.Warning($"[KitsuneCommand] Native library not found: {fullPath}");
                return;
            }

            IntPtr handle;
            if (PlatformHelper.IsLinux)
                handle = dlopen(fullPath, RTLD_NOW | RTLD_GLOBAL);
            else
                handle = LoadLibrary(fullPath);

            if (handle != IntPtr.Zero)
                Log.Out($"[KitsuneCommand] Pre-loaded native library: {libName}");
            else
                Log.Warning($"[KitsuneCommand] Failed to pre-load native library: {libName}");
        }
    }
}
