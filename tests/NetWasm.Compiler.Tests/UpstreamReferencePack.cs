using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// The .NET 11.0.0 Microsoft.NETCore.App.Ref beside the SDK that runs the tests, the release NetWasm.App's sources
// are ported from (eng/csharp15-toolchain.json's browserRuntimeFrameworkVersion).
internal static class UpstreamReferencePack
{
    public static string Directory()
    {
        // The shared runtime is at <dotnet>/shared/Microsoft.NETCore.App/<version>.
        var dotnet = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."));
        using var toolchain = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(TestAssets.FindRepositoryRoot(), "eng", "csharp15-toolchain.json")));
        var version = toolchain.RootElement.GetProperty("browserRuntimeFrameworkVersion").GetString()!;
        var pack = Path.Combine(dotnet, "packs", "Microsoft.NETCore.App.Ref", version);
        Assert.True(System.IO.Directory.Exists(pack), $"No .NET {version} reference pack at {pack}.");
        return Path.Combine(pack, "ref", "net11.0");
    }

    // The same release's shared runtime, whose implementation assemblies carry what the reference pack leaves out
    // (serializability, type forwarding history and implementation attributes).
    public static string RuntimeDirectory()
    {
        var reference = Directory();
        var version = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(reference))!)!;
        var dotnet = Path.GetFullPath(Path.Combine(reference, "..", "..", "..", "..", ".."));
        var runtime = Path.Combine(dotnet, "shared", "Microsoft.NETCore.App", version);
        Assert.True(System.IO.Directory.Exists(runtime), $"No .NET {version} shared runtime at {runtime}.");
        return runtime;
    }
}
