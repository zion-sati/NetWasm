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
    };

    // A result without diagnostic artifacts names no exception types; one with them names its map's.
    [Fact]
    public void TheFactsNameTheExceptionTypesOfTheResultsDiagnostics()
    {
        var result = new ModuleCompilationResult([0x00, 0x61, 0x73, 0x6D], null!, 64);

        Assert.Null(KeptCompilationFacts.Of(Base, result).ExceptionTypes);
        Assert.Empty(KeptCompilationFacts.Of(Base, result with
        {
            DiagnosticArtifacts = new NetWasm.Compiler.ExceptionTypes.DiagnosticArtifactBundle([], null!, [], null!),
        }).ExceptionTypes!.Value);
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
