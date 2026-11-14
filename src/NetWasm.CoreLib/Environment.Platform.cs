namespace System;

public static partial class Environment
{
    public static bool UserInteractive => throw UnsupportedEnvironment("interactive-session detection");
    public static int SystemPageSize => throw UnsupportedEnvironment("system page-size queries");
    public static long WorkingSet => throw UnsupportedEnvironment("process working-set queries");
    // Upstream's Browser and WASI builds (Environment.Browser.cs) report an "Other" platform at version 1.0.0.0.
    private static OperatingSystem? s_osVersion;
    public static OperatingSystem OSVersion => s_osVersion ??= new OperatingSystem(PlatformID.Other, new Version(1, 0, 0, 0));
    // NetWasm reports its own product version, without prerelease or build metadata, rather than
    // .NET's (owner decision 2026-10-20).
    public static Version Version
    {
        get
        {
            var value = NetWasmProductVersion.Value;
            var end = value.IndexOfAny(new[] { '-', '+' });
            return new Version(end < 0 ? value : value.Substring(0, end));
        }
    }
    public static string CommandLine => throw UnsupportedEnvironment("the original host command-line string");
    public static string StackTrace => throw UnsupportedEnvironment("current-stack capture");
    public static string SystemDirectory => throw UnsupportedEnvironment("host system-directory discovery");

    public static string[] GetLogicalDrives() => IO.Directory.GetLogicalDrives();


    private static PlatformNotSupportedException UnsupportedEnvironment(string operation) =>
        new("NetWasm does not currently provide " + operation + ".");
}
