using System.Collections.Immutable;
using NetWasm.Compiler.Pipeline;

namespace NetWasm.Compiler.Tests.Units;

// The early cutoff serves a compile only from one with the same options: every option that
// can change the module changes the fingerprint, and a WIT file is taken by its content.
public sealed class KeptCompilationFactsTests
{
    private static readonly CompilerOptions Base = new("app.dll", ["corelib.dll"], "EntryPoint", "Run", []);

    public static TheoryData<string> Changes => new()
    {
        "export", "entry token", "diagnostic trace", "world", "source", "alias", "import module", "stack trace",
        "feature switch", "runtime property", "native library",
    };

    // The facts keep the result's sidecars, which a compile served from them writes again.
    [Fact]
    public void TheFactsKeepTheResultsSidecars()
    {
        var result = new ModuleCompilationResult([0x00, 0x61, 0x73, 0x6D], null!, 64);

        Assert.Null(KeptCompilationFacts.Of(Base, result).TypeSymbols);
        var facts = KeptCompilationFacts.Of(Base, result with
        {
            TypeSymbols = new(NetWasm.Compiler.Symbols.SymbolSidecarKind.Type, [1, 2]),
            MethodSymbols = new(NetWasm.Compiler.Symbols.SymbolSidecarKind.Method, [3]),
        });
        Assert.Equal([1, 2], facts.TypeSymbols);
        Assert.Equal([3], facts.MethodSymbols);
        var read = KeptCompilationFacts.Read(KeptCompilationFacts.Write(facts))!;
        Assert.Equal([1, 2], read.TypeSymbols);
        Assert.Equal([3], read.MethodSymbols);
        Assert.Equal(64, KeptCompilationFacts.Of(Base, result).StaticDataEnd);
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public void EveryOptionThatShapesTheModuleChangesTheFingerprint(string change)
    {
        var changed = change switch
        {
            "export" => Base with { Exports = [new RequestedExport("run", "EntryPoint", "Run")] },
            "entry token" => Base with { EntryMethodToken = 0x06000001 },
            "diagnostic trace" => Base with { DiagnosticTracePath = "trace.json" },
            "world" => Base with { WitWorld = "app" },
            "source" => Base with { SourcePaths = ["Program.cs"] },
            "alias" => Base with { ReferenceAssemblyAliases = ImmutableDictionary<string, string>.Empty.Add("System.Runtime", "NetWasm.CoreLib") },
            "import module" => Base with { ImportModules = ["host"] },
            "feature switch" => Base with { FeatureSwitches = ImmutableDictionary<string, bool>.Empty.Add("Feature", true) },
            "runtime property" => Base with { RuntimeProperties = [("Name", "Value")] },
            "native library" => Base with { LinkedNativeLibraries = ["libz"] },
            _ => Base with { EmitStackTrace = true },
        };

        Assert.NotEqual(CompilationOptionsFingerprint.Of(Base), CompilationOptionsFingerprint.Of(changed));
    }

    [Fact]
    public void AWitFileIsTakenByItsContent()
    {
        var wit = Path.Combine(Path.GetTempPath(), "netwasm-fingerprint-" + Guid.NewGuid().ToString("N") + ".wit");
        try
        {
            var options = Base with { WitPath = wit };
            var missing = CompilationOptionsFingerprint.Of(options);
            File.WriteAllText(wit, "package app:one;");
            var first = CompilationOptionsFingerprint.Of(options);
            File.WriteAllText(wit, "package app:two;");

            Assert.NotEqual(missing, first);
            Assert.NotEqual(first, CompilationOptionsFingerprint.Of(options));
        }
        finally
        {
            File.Delete(wit);
        }
    }

    [Fact]
    public void NoFactsAreReadFromNothingOrFromAnotherShape()
    {
        Assert.Null(KeptCompilationFacts.Read([]));
        Assert.Null(KeptCompilationFacts.Read("{\"options\":"u8));
    }
}
