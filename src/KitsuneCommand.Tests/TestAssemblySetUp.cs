// Assembly-wide NUnit fixture. `[SetUpFixture]` outside any namespace
// runs OnceTimeSetUp before any test in the assembly, OnceTimeTearDown
// after the last one — perfect spot for "make sqlite work in tests."
//
// Why this exists:
//
// System.Data.SQLite is built from source with SQLITE_STANDARD, so
// its P/Invoke target is "sqlite3" (matching what sqlite.org's
// official sqlite3.dll exports). At production runtime, ModEntry.cs
// calls LoadLibrary on <mod-root>/x64/sqlite3.dll BEFORE any SQLite
// connection opens — that pins the native handle so DllImport
// finds it in the process's module table.
//
// Tests don't go through ModEntry.cs (no 7DTD process, no mod load).
// Without preloading, the first SQLiteConnection.Open() throws
// DllNotFoundException because Windows' DllImport default search
// only looks in the EXE dir + System32 + PATH — not in x64/
// subdirectories.
//
// This fixture does the same LoadLibrary dance from
// <test-bin>/x64/sqlite3.dll. KitsuneCommand.csproj copies the DLL
// into bin/<config>/x64/, and ProjectReference propagates that
// content into the test project's bin too — so the path resolves
// without any post-build script.
//
// On Linux: nothing to do. Mono's global config maps sqlite3 ->
// libsqlite3.so.0 from the system package.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;

[SetUpFixture]
public class TestAssemblySetUp
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [OneTimeSetUp]
    public void PreloadNativeDependencies()
    {
        // Linux: Mono maps "sqlite3" to libsqlite3.so.0 automatically;
        // no preload needed. PlatformID.Unix covers Linux + macOS.
        if (Environment.OSVersion.Platform == PlatformID.Unix ||
            Environment.OSVersion.Platform == PlatformID.MacOSX)
        {
            return;
        }

        // Resolve <test-bin>/x64/sqlite3.dll. Assembly.Location points
        // at the test DLL; its directory is the test bin root.
        var binDir = Path.GetDirectoryName(typeof(TestAssemblySetUp).Assembly.Location);
        if (string.IsNullOrEmpty(binDir)) return;

        var sqliteDll = Path.Combine(binDir, "x64", "sqlite3.dll");
        if (!File.Exists(sqliteDll))
        {
            // Surface a clear failure rather than silently letting
            // DllNotFoundException explode on the first SQLite call.
            // If this trips, the most likely cause is the
            // KitsuneCommand.csproj <None> copy rule for x64/sqlite3.dll
            // got removed or the source file is missing.
            throw new FileNotFoundException(
                "Test setup: native sqlite3.dll not found at " + sqliteDll +
                ". Check that KitsuneCommand.csproj still has the " +
                "<None Include=\"x64\\sqlite3.dll\" CopyToOutputDirectory=...> entry.");
        }

        // LoadLibrary pins the module by full path; subsequent
        // DllImport("sqlite3") resolves via the process's loaded-
        // modules table without needing the directory on PATH.
        // Mirrors PreloadNativeLibrary in src/KitsuneCommand/Core/ModEntry.cs.
        var handle = LoadLibrary(sqliteDll);
        if (handle == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                "Test setup: LoadLibrary failed for " + sqliteDll +
                " (Win32 error " + err + "). The DLL is present but " +
                "could not be loaded — likely a bitness mismatch " +
                "(test host is " + (Environment.Is64BitProcess ? "x64" : "x86") +
                ", DLL ships x64).");
        }
    }
}
