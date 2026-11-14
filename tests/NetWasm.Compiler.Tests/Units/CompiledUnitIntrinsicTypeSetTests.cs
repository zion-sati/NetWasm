using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Units;

// RunClassConstructor, GetUninitializedObject, Array.CreateInstance and MemberwiseClone are
// emitted as a dispatch over every type the whole program can produce at run time. The methods
// that call them live in a library whose assemblies a later compile leaves alone, while another
// library adds a type to the program: their kept units are reusable by assembly shape, so only the
// whole-program type tables they recorded can stop a stale dispatch. Every compile must equal a
// plain compile of the same program.
public sealed class CompiledUnitIntrinsicTypeSetTests
{
    private const string LibrarySource = """
        using System;
        using System.Runtime.CompilerServices;

        namespace Library;

        public class Base
        {
            public int Value = 1;

            public Base CloneSelf() => (Base)MemberwiseClone();
        }

        public static class Probes
        {
            public static void Initialize(RuntimeTypeHandle handle) => RuntimeHelpers.RunClassConstructor(handle);

            public static object Uninitialized(Type type) => RuntimeHelpers.GetUninitializedObject(type);

            public static int Length(Type type) => Array.CreateInstance(type, 3).Length;

            public static int Cloned(Base value) => value.CloneSelf().Value;
        }
        """;

    private const string ExtraSource = """
        namespace Extra;

        public sealed class Added : Library.Base
        {
            public static int Seen;

            public int More = 2;

            static Added() => Seen = 7;
        }
        """;

    private const string Before = """
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Library.Probes.Initialize(typeof(Library.Base).TypeHandle);
                var created = Library.Probes.Uninitialized(typeof(Library.Base));
                return Library.Probes.Length(typeof(Library.Base)) +
                    Library.Probes.Cloned(new Library.Base()) + (created is Library.Base ? 1 : 0);
            }
        }
        """;

    private const string After = """
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Library.Probes.Initialize(typeof(Library.Base).TypeHandle);
                Library.Probes.Initialize(typeof(Extra.Added).TypeHandle);
                var created = Library.Probes.Uninitialized(typeof(Extra.Added));
                return Library.Probes.Length(typeof(Extra.Added)) +
                    Library.Probes.Cloned(new Extra.Added()) + (created is Extra.Added ? 1 : 0) + Extra.Added.Seen;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void IntrinsicDispatchesFollowTheProgramsTypes(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource("Intrinsics.Library", LibrarySource);
        var extra = assets.CompileSource("Intrinsics.Extra", ExtraSource, library);
        var units = Path.Combine(assets.Directory, "obj");
        var summary = Path.Combine(units, "netwasm-units", "last-compile.txt");
        foreach (var (step, source) in new[] { ("cold", Before), ("added", After), ("back", Before) })
        {
            var app = step == "added"
                ? assets.CompileSource("Intrinsics.Application", source, library, extra)
                : assets.CompileSource("Intrinsics.Application", source, library);
            var options = new CompilerOptions(app,
                step == "added" ? [assets.CoreLib, library, extra] : [assets.CoreLib, library],
                "EntryPoint", "Run", [], Target: target, Optimization: FinalWasmOptimization.O0);
            var plain = NetWasmCompiler.Compile(options).ApplicationModule;
            var restored = NetWasmCompiler.Compile(options with
            {
                IntermediateOutputPath = units,
                CompiledUnits = true,
                VerifyCompiledUnits = true,
            }).ApplicationModule;

            Assert.True(plain.AsSpan().SequenceEqual(restored), $"{step}: the module differs from a plain compile");
            if (step == "cold") continue;
            var reuse = File.ReadAllText(summary);
            Assert.False(reuse.StartsWith("restored 0 ", StringComparison.Ordinal), $"{step}: nothing was restored: {reuse}");
        }
    }
}
