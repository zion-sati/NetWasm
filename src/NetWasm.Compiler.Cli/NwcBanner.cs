using System;
using System.Reflection;

namespace NetWasm.Compiler.Cli;

// The two lines csc prints first, with nwc's version and source commit.
internal static class NwcBanner
{
    public static void Write(IConsole console, string version)
    {
        ArgumentNullException.ThrowIfNull(console);
        var separator = version.IndexOf('+', StringComparison.Ordinal);
        console.WriteLine(separator < 0
            ? $"NetWasm Compiler version {version}"
            : $"NetWasm Compiler version {version[..separator]} ({Commit(version[(separator + 1)..])})");
        console.WriteLine("Copyright (C) 2026 Zion Sati. All rights reserved.");
        console.WriteLine(string.Empty);
    }

    // The SDK writes the version and source commit into every assembly it builds.
    public static string InformationalVersion() => typeof(NwcBanner).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    private static string Commit(string commit) => commit.Length > 8 ? commit[..8] : commit;
}
