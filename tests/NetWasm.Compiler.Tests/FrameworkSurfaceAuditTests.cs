using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// NetWasm.App's public surface, library by library, against the same assemblies in the .NET 11.0.0 reference pack. The
// recorded audit (FrameworkSurfaceAudit.tsv, beside this file) lists every difference,
// so a change to NetWasm.App's API, or to the upstream pack, shows up as a reviewed diff.
public sealed class FrameworkSurfaceAuditTests
{
    private const string UpdateVariable = "NETWASM_UPDATE_SURFACE_AUDIT";

    [Fact]
    public void NetWasmAppMatchesTheRecordedAudit()
    {
        var root = TestAssets.FindRepositoryRoot();
        var recordedPath = Path.Combine(root, "tests", "NetWasm.Compiler.Tests", "FrameworkSurfaceAudit.tsv");
        var coreLib = Path.Combine(root, "src", "NetWasm.CoreLib", "bin", "Release", "net10.0", "ref", "NetWasm.CoreLib.dll");
        var audit = FrameworkSurfaceAudit.Compute(
            [.. TestAssets.FrameworkAssemblies(root).Select(path => MetadataReference.CreateFromFile(path))],
            [MetadataReference.CreateFromFile(coreLib)],
            [.. Directory.EnumerateFiles(UpstreamReferencePack.Directory(), "*.dll").Order().Select(path => MetadataReference.CreateFromFile(path))]);
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(recordedPath, string.Join('\n', audit) + "\n");
        }

        var recorded = File.ReadAllLines(recordedPath);
        var added = audit.Except(recorded, StringComparer.Ordinal).ToList();
        var removed = recorded.Except(audit, StringComparer.Ordinal).ToList();
        Assert.True(added.Count == 0 && removed.Count == 0,
            $"NetWasm.App's surface differs from the recorded audit.\nNew rows:\n{string.Join('\n', added)}\n" +
            $"Rows no longer found:\n{string.Join('\n', removed)}\nReview the change, then rerun with {UpdateVariable}=1 to record it.");
    }

    // One library with each kind of difference; the type it forwards is not reported missing.
    [Fact]
    public void ReportsEachKindOfDifference()
    {
        var core = Compile("Core", "namespace W { public class B { } }");
        var library = Compile("System.Widgets", """
            [assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(W.B))]
            namespace W
            {
                public class A { public void M() { } public void X() { } protected void P() { } internal void I() { } }
                public class C { }
                public class D { }
            }
            """, core);
        var upstream = Compile("System.Widgets", """
            namespace W
            {
                public class A { public void M() { } public void N() { } protected void P() { } }
                public class B { }
                public class E { }
            }
            """);
        var other = Compile("System.Other", "namespace W { public class C { } }");

        var rows = FrameworkSurfaceAudit.Compute([library], [core, .. Runtime()], [upstream, other, .. Runtime()]);

        Assert.Equal(
        [
            FrameworkSurfaceAudit.Header,
            "System.Widgets\textra-member\tM:W.A.X\t",
            "System.Widgets\tmissing-member\tM:W.A.N\t",
            "System.Widgets\tmissing-type\tT:W.E\t",
            "System.Widgets\tnot-upstream\tT:W.D\t",
            "System.Widgets\tupstream-elsewhere\tT:W.C\tSystem.Other",
        ], rows);
    }

    private static PortableExecutableReference Compile(string name, string source, params MetadataReference[] references)
    {
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], [.. Runtime(), .. references],
            new(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        Assert.True(result.Success, string.Join('\n', result.Diagnostics));
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private static IEnumerable<MetadataReference> Runtime() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is "System.Runtime.dll" or "System.Private.CoreLib.dll")
            .Select(path => MetadataReference.CreateFromFile(path));
}
