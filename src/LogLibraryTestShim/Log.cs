// Test-only stand-in for the game's LogLibrary (see LogLibraryTestShim.csproj).
// Mirrors the public surface of refs/LogLibrary.dll exactly; bodies write to
// the console so log lines show up in the test output.

using System;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

[assembly: AssemblyTitle("LogLibrary (test shim)")]
[assembly: AssemblyVersion("0.0.0.0")]

public static class Log
{
    public delegate void LogCallbackDelegate(string _msg, string _trace, LogType _type);

    public delegate void LogCallbackExtendedDelegate(string _formattedMessage, string _plainMessage, string _trace,
        LogType _type, DateTime _timestamp, long _uptime);

    public static event LogCallbackDelegate LogCallbacks;
    public static event LogCallbackExtendedDelegate LogCallbacksExtended;

    public static void AddOutputPath(string _path) { }

    public static void WriteLine(string _logString) => Console.WriteLine(_logString);

    public static void Out(string _txt) => Console.WriteLine("INF " + _txt);

    public static void Warning(string _txt) => Console.WriteLine("WRN " + _txt);

    public static void Error(string _txt) => Console.WriteLine("ERR " + _txt);

    public static void Exception(Exception _e) => Console.WriteLine("EXC " + _e);

    public static void Out(string _format, params object[] _values) => Out(string.Format(_format, _values));

    public static void Warning(string _format, params object[] _values) => Warning(string.Format(_format, _values));

    public static void Error(string _format, params object[] _values) => Error(string.Format(_format, _values));

    // Keeps the compiler from flagging the events as never used; tests that
    // need to observe callbacks can subscribe and raise them through here.
    internal static void Raise(string msg, string trace, LogType type)
    {
        LogCallbacks?.Invoke(msg, trace, type);
        LogCallbacksExtended?.Invoke(msg, msg, trace, type, DateTime.Now, 0);
    }
}

public class MicroStopwatch : Stopwatch
{
    public MicroStopwatch() { }

    public MicroStopwatch(bool _bStart)
    {
        if (_bStart) Start();
    }

    public long ElapsedMicroseconds => ElapsedTicks * 1_000_000L / Frequency;

    public static MicroStopwatch a() => new MicroStopwatch(true);

    public void ResetAndRestart()
    {
        Reset();
        Start();
    }
}
