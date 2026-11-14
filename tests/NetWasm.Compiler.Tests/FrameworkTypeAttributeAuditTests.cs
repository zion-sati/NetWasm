using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// The type-level metadata of NetWasm's CoreLib and NetWasm.App against the .NET 11.0.0 shared runtime. The recorded
// audit (FrameworkTypeAttributeAudit.tsv, beside this file) lists the differences that remain on purpose or are still
// to port, so an attribute a port drops, or one it adds, shows up as a reviewed diff.
public sealed class FrameworkTypeAttributeAuditTests
{
    private const string UpdateVariable = "NETWASM_UPDATE_TYPE_ATTRIBUTE_AUDIT";

    [Fact]
    public void NetWasmTypesMatchTheRecordedAudit()
    {
        var root = TestAssets.FindRepositoryRoot();
        var recordedPath = Path.Combine(root, "tests", "NetWasm.Compiler.Tests", "FrameworkTypeAttributeAudit.tsv");
        var coreLib = Path.Combine(root, "src", "NetWasm.CoreLib", "bin", "Release", "net10.0", "NetWasm.CoreLib.dll");
        var audit = FrameworkTypeAttributeAudit.Compute(
            [coreLib, .. TestAssets.FrameworkAssemblies(root)],
            Directory.EnumerateFiles(UpstreamReferencePack.RuntimeDirectory(), "*.dll").Order(StringComparer.Ordinal));
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(recordedPath, string.Join('\n', audit) + "\n");
        }

        var recorded = File.ReadAllLines(recordedPath);
        var added = audit.Except(recorded, StringComparer.Ordinal).ToList();
        var removed = recorded.Except(audit, StringComparer.Ordinal).ToList();
        Assert.True(added.Count == 0 && removed.Count == 0,
            $"NetWasm's type attributes differ from the recorded audit.\nNew rows:\n{string.Join('\n', added)}\n" +
            $"Rows no longer found:\n{string.Join('\n', removed)}\nReview the change, then rerun with {UpdateVariable}=1 to record it.");
    }

    // One type for each kind of difference.
    [Fact]
    public void ReportsEachKindOfDifference()
    {
        using var directory = new TemporaryDirectory();
        var ours = Compile(directory.Path, "Ours", """
            namespace W
            {
                public sealed class A { }
                public class B { }
                [System.Obsolete("old")] public class C { }
                [System.Diagnostics.DebuggerDisplay("d")] public class D { }
                public enum E { X }
                public class F { public static readonly int V; static F() { V = 1; } }
                public struct G { }
            }
            """);
        var upstream = Compile(directory.Path, "Upstream", """
            namespace W
            {
                public class A { }
                [System.Serializable] public class B { }
                [System.Obsolete("older")] public class C { }
                public class D { }
                [System.Flags] public enum E { X }
                public class F { public static readonly int V = 1; }
                [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)] public struct G { }
            }
            """);

        Assert.Equal(
        [
            FrameworkTypeAttributeAudit.Header,
            "arguments\tW.C\tSystem.ObsoleteAttribute(\"old\") upstream (\"older\")",
            "beforefieldinit-missing\tW.F\t",
            "extra-cosmetic\tW.D\tSystem.Diagnostics.DebuggerDisplayAttribute",
            "layout\tW.G\tsequential upstream auto",
            "missing\tW.E\tSystem.FlagsAttribute",
            "sealed-extra\tW.A\t",
            "serializable-missing\tW.B\t",
        ], FrameworkTypeAttributeAudit.Compute([ours], [upstream]));
    }

    private static string Compile(string directory, string name, string source)
    {
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], Runtime(),
            new(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(directory, name + ".dll");
        var result = compilation.Emit(path);
        Assert.True(result.Success, string.Join('\n', result.Diagnostics));
        return path;
    }

    private static IEnumerable<MetadataReference> Runtime() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is "System.Runtime.dll" or "System.Private.CoreLib.dll")
            .Select(path => MetadataReference.CreateFromFile(path));

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Directory.CreateDirectory(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "netwasm-attribute-audit-" + Guid.NewGuid().ToString("N"))).FullName;

        public void Dispose() => System.IO.Directory.Delete(Path, recursive: true);
    }
}
