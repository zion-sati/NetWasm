using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Units;

// Kept compiled units never stand in for a changed input: a compile whose implementation,
// reference view (facade) or assembly alias changed, or whose package provenance changed,
// fails exactly as a compile without units does, and never reports the old provenance. Both
// compile without optimization, as the frontend cache's parity tests did; the kept and cold
// compiles report a broken library callee alike, from the callee.
public sealed class CompiledUnitInputChangeTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, "implementation")]
    [InlineData(WasmTarget.Wasm64, "implementation")]
    [InlineData(WasmTarget.Wasm32, "facade")]
    [InlineData(WasmTarget.Wasm64, "facade")]
    [InlineData(WasmTarget.Wasm32, "alias")]
    [InlineData(WasmTarget.Wasm64, "alias")]
    public void ChangedSemanticInputsCannotRestorePreviouslyValidLibraryCode(WasmTarget target, string change)
    {
        using var assets = TestAssets.Create();
        var contract = assets.CompileSource("Mutable.Contract", """
            namespace Shared;
            public static class Api { public static int Read() => 42; }
            """);
        var provider = assets.CompileSource("Mutable.Provider", """
            namespace Shared;
            public static class Api { public static int Read() => 42; }
            """);
        var missing = assets.CompileSource("Missing.Provider", """
            namespace Shared;
            public static class Api { public static int Read() => 43; }
            """);
        var library = assets.CompileSource("Mutable.Library", change == "implementation" ? """
            namespace Library;
            public static class Bridge { public static int Read() => 42; }
            """ : """
            namespace Library;
            public static class Bridge { public static int Read() => Shared.Api.Read(); }
            """, contract);
        var app = assets.CompileSource("Mutable.Application", """
            namespace Application;
            public static class Entry { public static int Run(int input) => Library.Bridge.Read(); }
            """, library);
        if (change == "facade")
            assets.CompileSource("Mutable.Contract", """
                [assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Shared.Api))]
                """, provider);
        var options = new CompilerOptions(app,
            change == "implementation" ? [assets.CoreLib, library] : [assets.CoreLib, library, provider],
            "Application.Entry", "Run", [], target, Optimization: FinalWasmOptimization.None,
            MetadataReferencePaths: change == "facade" ? [contract] : [],
            ReferenceAssemblyAliases: change == "alias"
                ? ImmutableDictionary<string, string>.Empty.Add("Mutable.Contract", "Mutable.Provider") : null)
        {
            IntermediateOutputPath = Path.Combine(assets.Directory, "obj"),
            CompiledUnits = true,
        };
        Assert.NotEmpty(NetWasmCompiler.Compile(options).ApplicationModule);

        if (change == "implementation")
            assets.CompileSource("Mutable.Library", """
                namespace Library;
                public static class Bridge { public static int Read() => Shared.Api.Read(); }
                """, missing);
        else if (change == "facade")
            assets.CompileSource("Mutable.Contract", """
                [assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Shared.Api))]
                """, missing);
        else
            options = options with
            {
                ReferenceAssemblyAliases = ImmutableDictionary<string, string>.Empty.Add("Mutable.Contract", "Missing.Provider"),
            };

        var kept = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));
        var cold = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options with
        {
            IntermediateOutputPath = null,
            CompiledUnits = false,
        }));
        Assert.Equal(DiagnosticCode.AssemblyResolution, kept.Diagnostic.Code);
        Assert.Equal(cold.Diagnostic.Code, kept.Diagnostic.Code);
        Assert.Equal(cold.Diagnostic.Message, kept.Diagnostic.Message);
        Assert.Equal(cold.Diagnostic.Context, kept.Diagnostic.Context);
        Assert.True(cold.Diagnostic.DependencyChain.SequenceEqual(kept.Diagnostic.DependencyChain));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void KeptUnitsDoNotBypassRejectionOrReuseOldPackageProvenance(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var dependency = assets.CompileSource("Cache.Dependency", """
            namespace Dependency;
            public static class Api { public static int Read() => 42; }
            """);
        var library = assets.CompileSource("Cache.Library", """
            namespace Library;
            public static class Api
            {
                public static int Safe(int input) => input + 1;
                public static int Broken() => Dependency.Api.Read();
            }
            """, dependency);
        var app = assets.CompileSource("Cache.Application", Application(false), library, dependency);
        var options = new CompilerOptions(app, [assets.CoreLib, library],
            "Application.Entry", "Run", [], target, Optimization: FinalWasmOptimization.None)
        {
            IntermediateOutputPath = Path.Combine(assets.Directory, "obj"),
            CompiledUnits = true,
            ReferenceOrigins = [new(library, CompilationReferenceRole.Implementation,
                "Previous.Package", "1.0.0", "lib/netstandard2.1/Cache.Library.dll")],
        };
        Assert.NotEmpty(NetWasmCompiler.Compile(options).ApplicationModule);

        assets.CompileSource("Cache.Application", Application(true), library, dependency);
        options = options with
        {
            ReferenceOrigins = [options.ReferenceOrigins[0] with { PackageId = "Current.Package", PackageVersion = "2.0.0" }],
        };
        var kept = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));
        var cold = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options with
        {
            IntermediateOutputPath = null,
            CompiledUnits = false,
        }));
        Assert.Equal(DiagnosticCode.AssemblyResolution, kept.Diagnostic.Code);
        Assert.Equal(cold.Diagnostic.Code, kept.Diagnostic.Code);
        Assert.Equal(cold.Diagnostic.Message, kept.Diagnostic.Message);
        Assert.Equal(cold.Diagnostic.Context, kept.Diagnostic.Context);
        Assert.Equal(cold.Diagnostic.Origin, kept.Diagnostic.Origin);
        Assert.True(cold.Diagnostic.DependencyChain.SequenceEqual(kept.Diagnostic.DependencyChain));
        Assert.DoesNotContain("Previous.Package", kept.Message, StringComparison.Ordinal);
        Assert.NotEmpty(kept.Diagnostic.DependencyChain);
    }

    private static string Application(bool broken) => $$"""
        namespace Application;
        public static class Entry
        {
            public static int Run(int input) => {{(broken ? "Safe(input) + Broken()" : "Safe(input)")}};
            private static int Safe(int input) => Library.Api.Safe(input);
            private static int Broken() => Library.Api.Broken();
        }
        """;
}
