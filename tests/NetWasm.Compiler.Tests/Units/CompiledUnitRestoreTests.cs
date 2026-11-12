using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Units;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Units;

// Compiled units restored across edits must give the module a plain compile gives.
// Every compile verifies: each restored method is emitted as well, and a body that
// differs from its unit's fails the compile.
public sealed class CompiledUnitRestoreTests
{
    private const string Base = """
        using System;
        using System.Collections.Generic;

        public struct Point
        {
            public int X;
            public int Y;
            public int Sum() => X + Y;
        }

        public sealed class Counter
        {
            private static int s_created;
            private readonly List<int> _values = new();
            public Counter() { s_created++; }
            public void Add(int value) => _values.Add(value);
            public int Total()
            {
                var total = 0;
                foreach (var value in _values) total += value;
                return total;
            }
            public static int Created => s_created;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var counter = new Counter();
                for (var i = 0; i < input; i++) counter.Add(i * 3);
                var point = new Point { X = input, Y = 2 };
                Func<int, int> twice = x => x * 2;
                return counter.Total() + point.Sum() + twice(Counter.Created) + Text(input).Length +
                    Nested(input) + Filtered(input);
            }

            private static int Nested(int input)
            {
                var result = 0;
                try
                {
                    try
                    {
                        result = checked(input * 1000000);
                    }
                    catch (OverflowException)
                    {
                        result = -1;
                    }
                    catch (ArgumentException)
                    {
                        result = -2;
                    }
                    finally
                    {
                        result++;
                    }
                }
                catch (Exception)
                {
                    result = -3;
                }
                return result;
            }

            private static int Filtered(int input)
            {
                try
                {
                    return input > 50 ? throw new ArgumentException("large") : input;
                }
                catch (ArgumentException) when (input > 60)
                {
                    return -1;
                }
            }

            private static string Text(int input)
            {
                try
                {
                    return input > 100 ? throw new InvalidOperationException("big") : "small:" + input;
                }
                catch (InvalidOperationException exception)
                {
                    return exception.Message;
                }
            }
        }
        """;

    internal static string BaseSource => Base;

    // One method's body changes.
    private static readonly string BodyEdit = Base.Replace("i * 3", "i * 5", StringComparison.Ordinal);

    // Layouts, static data, strings, exception metadata and function indices all move.
    private static readonly string ShapeEdit = Base
        .Replace("    public int X;", "    public long Z;\n    public int X;", StringComparison.Ordinal)
        .Replace("    private static int s_created;", "    private static long s_extra;\n    private static int s_created;",
            StringComparison.Ordinal)
        .Replace("public Counter() { s_created++; }", "public Counter() { s_created++; s_extra += 2; }",
            StringComparison.Ordinal)
        .Replace("\"big\"", "\"bigger\"", StringComparison.Ordinal)
        .Replace("+ Text(input).Length;", "+ Text(input).Length + Guarded(input);", StringComparison.Ordinal)
        .Replace("    private static string Text(int input)",
            "    private static int Guarded(int input)\n" +
            "    {\n" +
            "        try { return checked(input * 1000); }\n" +
            "        catch (OverflowException) { return -1; }\n" +
            "        finally { Counter.Created.ToString(); }\n" +
            "    }\n\n" +
            "    private static string Text(int input)", StringComparison.Ordinal);

    // Finalizers, interface and generic virtual dispatch, delegates and stack traces: the
    // edit adds a finalizable type, a delegate type, an implementation and a delegate target, which move type ids, dispatch targets, stack-trace ids and plans.
    private const string Runtime = """
        using System;
        using System.Collections.Generic;
        public interface IShape { int Area(); }
        public sealed class Square(int side) : IShape { public int Area() => side * side; }
        public sealed class Rect(int w, int h) : IShape { public int Area() => w * h; }

        public sealed class Resource
        {
            public static int Finalized;
            ~Resource() { Finalized++; }
        }

        public abstract class Visitor
        {
            public abstract T Visit<T>(T value);
        }

        public sealed class EchoVisitor : Visitor
        {
            public override T Visit<T>(T value) => value;
        }

        public static class Reflected
        {
            public static int Twice(int value) => value * 2;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var shapes = new List<IShape> { new Square(input), new Rect(input, 3) };
                var total = 0;
                foreach (var shape in shapes) total += shape.Area();
                _ = new Resource();
                Visitor visitor = new EchoVisitor();
                total += visitor.Visit(input) + visitor.Visit("text").Length;
                Func<int, int> add = value => value + total;
                Func<int, int> scale = Reflected.Twice;
                total += scale(input);
                try
                {
                    if (input < 0) throw new ArgumentException("negative");
                }
                catch (ArgumentException exception)
                {
                    total += exception.StackTrace?.Length ?? 0;
                }
                return add(total) + Resource.Finalized;
            }
        }
        """;

    private static readonly string RuntimeEdit = Runtime
        .Replace("public sealed class Rect(int w, int h) : IShape { public int Area() => w * h; }",
            "public sealed class Rect(int w, int h) : IShape { public int Area() => w * h; }\n" +
            "public sealed class Circle(int r) : IShape { public int Area() => 3 * r * r; }\n" +
            "public sealed class Handle { ~Handle() { Resource.Finalized += 2; } }",
            StringComparison.Ordinal)
        .Replace("new Rect(input, 3) };", "new Rect(input, 3), new Circle(input) };", StringComparison.Ordinal)
        .Replace("foreach (var shape in shapes) total += shape.Area();",
            "foreach (var shape in shapes) total += shape.Area();\n" +
            "        _ = new Handle();\n" +
            "        Predicate<int> positive = value => value > 0;\n" +
            "        total += positive(input) ? 1 : 0;", StringComparison.Ordinal)
        .Replace("public static int Twice(int value) => value * 2;",
            "public static int Twice(int value) => value * 2;\n    public static int Thrice(int value) => value * 3;",
            StringComparison.Ordinal)
        .Replace("Func<int, int> scale = Reflected.Twice;", "Func<int, int> scale = input > 5 ? Reflected.Thrice : Reflected.Twice;",
            StringComparison.Ordinal);

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    public void RestoredUnitsGiveThePlainModuleAcrossEdits(WasmTarget target, bool stackTrace)
    {
        Verify(target, stackTrace, [
            ("cold", Base), ("unchanged", Base), ("body", BodyEdit), ("shape", ShapeEdit), ("back", Base),
        ]);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    public void RestoredUnitsGiveThePlainModuleAcrossRuntimeEdits(WasmTarget target, bool stackTrace)
    {
        // These edits change the program's shape, so its own units are not reused; with stack
        // traces on, the library's units name stack-trace symbols the edit moves, so nothing
        // is left to reuse there.
        Verify(target, stackTrace, [
            ("cold", Runtime), ("unchanged", Runtime), ("edit", RuntimeEdit), ("back", Runtime),
        ], step => step == "unchanged" || !stackTrace);
    }

    // The early cutoff's premise: when an edit keeps what its method adds to the program,
    // the new module is the last one with that method's body replaced, nothing else.
    [Fact]
    public void AnEditThatKeepsItsContributionChangesOnlyItsOwnBody()
    {
        using var assets = TestAssets.Create();
        var units = Path.Combine(assets.Directory, "obj");
        var kept = Path.Combine(units, "netwasm-units");
        CompilerOptions Options(string source) =>
            new(assets.CompileOptimizedSource("UnitRestore", source), [assets.CoreLib], "EntryPoint", "Run", [],
                Optimization: FinalWasmOptimization.O0)
            {
                IntermediateOutputPath = units,
                CompiledUnits = true,
            };
        _ = NetWasmCompiler.Compile(Options(Base));
        var before = File.ReadAllBytes(Path.Combine(kept, "module.wasm"));
        var functions = UnitFunctionIndices.Read(File.ReadAllBytes(Path.Combine(kept, "functions.bin")));
        var after = NetWasmCompiler.Compile(Options(BodyEdit)).ApplicationModule;
        var report = File.ReadAllText(Path.Combine(kept, "last-inputs.txt"));
        Assert.Contains("early cutoff: yes", report, StringComparison.Ordinal);

        // The report names the edited body's token; units name methods by it.
        var token = System.Text.RegularExpressions.Regex.Match(report, @"BodiesChanged 1 \((0x[0-9a-f]{8})\)").Groups[1].Value;
        var edited = functions.Functions.Single(pair => pair.Key.EndsWith("::" + token, StringComparison.Ordinal));
        var position = edited.Value - functions.ImportedFunctions;
        var patched = WasmCodePatcher.ReplaceBodies(before, new Dictionary<int, byte[]>
        {
            [position] = WasmCodePatcher.Bodies(after)![position],
        });

        Assert.NotEqual(before, after);
        Assert.True(after.AsSpan().SequenceEqual(patched), "the edit changed more than its method's body");
    }

    // A module compile the early cutoff serves patches the last module with the edited body
    // alone, and gives what a full compile gives; an edit it cannot serve runs in full.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheEarlyCutoffServesBodyEditsAsAFullCompileWould(bool stackTrace)
    {
        using var assets = TestAssets.Create();
        const string Name = "cutoff";
        var memory = new UnitStoreMemory();
        foreach (var (step, source, served) in new[]
                 {
                     ("cold", Base, false), ("body", BodyEdit, true), ("back", Base, true), ("unchanged", Base, true),
                     ("shape", ShapeEdit, false), ("again", Base, false), ("body again", BodyEdit, true),
                 })
        {
            var options = new CompilerOptions(assets.CompileOptimizedSource("UnitRestore", source), [assets.CoreLib],
                "EntryPoint", "Run", [], EmitStackTrace: stackTrace, Optimization: FinalWasmOptimization.O0);
            var plain = ModuleCompilationResult.Of(NetWasmCompiler.Compile(options));
            ModuleCompilationResult module;
            using (var services = new ServiceCollection().AddNetWasmCompiler().UseUnitMemory(memory).BuildServiceProvider())
            {
                module = services.GetRequiredService<INetWasmModuleCompiler>().Compile(options with
                {
                    IntermediateOutputPath = Name,
                    CompiledUnits = true,
                });
            }
            var report = memory.InputReport(Name) ?? "";
            Assert.True(served == module.PatchedLastCompile, $"{step}: {report}");
            // The account a host reads of the last compile: the cutoff's when it served it.
            var account = memory.Summary(Name) ?? "";
            Assert.True(served ? account == report : account.StartsWith("restored ", StringComparison.Ordinal),
                $"{step}: {account}");
            Assert.True(plain.ApplicationModule.AsSpan().SequenceEqual(module.ApplicationModule), $"{step}: the module differs");
            Assert.Equal(plain.StaticDataEnd, module.StaticDataEnd);
            Assert.Equal(plain.RuntimeFeatures, module.RuntimeFeatures);
            static string Imports(ModuleCompilationResult result) => string.Join(";", result.FunctionImports.Select(import =>
                $"{import.Module}.{import.Name}({string.Join(",", import.Type.Parameters)}){import.Type.Result}"));
            Assert.Equal(Imports(plain), Imports(module));
            Assert.Equal(plain.EntryPointToken, module.EntryPointToken);
            Assert.Equal(plain.SynchronousEntry, module.SynchronousEntry);
            Assert.Equal(plain.InteropManifest.Imports.Length, module.InteropManifest.Imports.Length);
            Assert.Equal(plain.InteropManifest.Exports.Length, module.InteropManifest.Exports.Length);
            Assert.Equal(plain.TypeSymbols!.Bytes, module.TypeSymbols!.Bytes);
            Assert.Equal(plain.MethodSymbols?.Bytes, module.MethodSymbols?.Bytes);
        }
    }

    // The early cutoff serves a compile only from one with the same options, so verification
    // replays only against such a compile: switching the target, stack traces or level
    // between compiles into one store verifies as any other compile does.
    [Fact]
    public void VerificationReplaysOnlyACompileTheCutoffWouldServe()
    {
        using var assets = TestAssets.Create();
        var units = Path.Combine(assets.Directory, "obj");
        var assembly = assets.CompileOptimizedSource("UnitRestore", Base);
        foreach (var (step, target, stackTrace) in new[]
                 {
                     ("wasm32", WasmTarget.Wasm32, false), ("wasm64", WasmTarget.Wasm64, false),
                     ("stack traces", WasmTarget.Wasm64, true), ("wasm32 again", WasmTarget.Wasm32, false),
                     ("unchanged", WasmTarget.Wasm32, false),
                 })
        {
            var options = new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
                Target: target, EmitStackTrace: stackTrace, Optimization: FinalWasmOptimization.O0);
            var plain = NetWasmCompiler.Compile(options).ApplicationModule;
            var restored = NetWasmCompiler.Compile(options with
            {
                IntermediateOutputPath = units,
                CompiledUnits = true,
                VerifyCompiledUnits = true,
            }).ApplicationModule;

            Assert.True(plain.AsSpan().SequenceEqual(restored), $"{step}: the module differs from a plain compile");
            var summary = File.ReadAllText(Path.Combine(units, "netwasm-units", "last-compile.txt"));
            // Only the compile the cutoff serves replays every method against the last one.
            Assert.Equal(step == "unchanged", summary.Contains("replayed ", StringComparison.Ordinal));
        }
    }

    // At an optimizing level analysis crosses methods, so units are restored under
    // verification but the early cutoff, which analyses edited methods alone, never applies.
    [Fact]
    public void UnitsAtAnOptimizingLevelRestoreButNeverCutOff()
    {
        using var assets = TestAssets.Create();
        const string Name = "optimized";
        var memory = new UnitStoreMemory();
        foreach (var (step, source) in new[] { ("cold", Base), ("body", BodyEdit), ("back", Base) })
        {
            var options = new CompilerOptions(assets.CompileOptimizedSource("UnitRestore", source), [assets.CoreLib],
                "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.Oz);
            var plain = NetWasmCompiler.Compile(options).ApplicationModule;
            ModuleCompilationResult module;
            using (var services = new ServiceCollection().AddNetWasmCompiler().UseUnitMemory(memory).BuildServiceProvider())
            {
                module = services.GetRequiredService<INetWasmModuleCompiler>().Compile(options with
                {
                    IntermediateOutputPath = Name,
                    CompiledUnits = true,
                    VerifyCompiledUnits = step == "body",
                });
            }
            Assert.False(module.PatchedLastCompile, step);
            Assert.True(plain.AsSpan().SequenceEqual(module.ApplicationModule), $"{step}: the module differs");
            var reuse = memory.Summary(Name) ?? "";
            if (step != "cold")
                Assert.False(reuse.StartsWith("restored 0 ", StringComparison.Ordinal), $"{step}: nothing was restored: {reuse}");
            if (step == "back")
                Assert.Contains("managed optimizations analyse across methods", memory.InputReport(Name), StringComparison.Ordinal);
        }
    }

    // A store another process replaced or damaged costs reuse, never the compile.
    [Fact]
    public void DamagedUnitsAreNotReused()
    {
        using var assets = TestAssets.Create();
        var units = Path.Combine(assets.Directory, "obj");
        var assembly = assets.CompileOptimizedSource("UnitRestore", Base);
        var options = new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.O0);
        var plain = NetWasmCompiler.Compile(options).ApplicationModule;
        var kept = options with { IntermediateOutputPath = units, CompiledUnits = true, VerifyCompiledUnits = true };
        _ = NetWasmCompiler.Compile(kept);
        var segments = Directory.GetFiles(Path.Combine(units, "netwasm-units"), "units-*.seg");
        Assert.NotEmpty(segments);
        foreach (var segment in segments)
        {
            var bytes = File.ReadAllBytes(segment);
            // Keep the header so the segment opens; scramble every record behind it.
            for (var index = 64; index < bytes.Length; index++) bytes[index] = (byte)(index * 31 + 7);
            File.WriteAllBytes(segment, bytes);
        }

        var restored = NetWasmCompiler.Compile(kept).ApplicationModule;

        Assert.True(plain.AsSpan().SequenceEqual(restored), "the module differs from a plain compile");
    }

    // As the browser compiles: one worker, units and inputs held in memory under a name,
    // and carried to a new store between compiles as a host carries them through storage.
    [Fact]
    public void RestoredUnitsGiveThePlainModuleWithOneWorkerAndUnitsInMemory()
    {
        using var assets = TestAssets.Create();
        const string Name = "session-units";
        var memory = new UnitStoreMemory();
        foreach (var (step, source) in new[] { ("cold", Base), ("unchanged", Base), ("body", BodyEdit), ("back", Base) })
        {
            var assembly = assets.CompileOptimizedSource("UnitRestore", source);
            var options = new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
                Optimization: FinalWasmOptimization.O0);
            var plain = NetWasmCompiler.Compile(options).ApplicationModule;
            byte[] restored;
            using (var services = InMemory(memory).BuildServiceProvider())
            {
                restored = services.GetRequiredService<INetWasmCompiler>().Compile(options with
                {
                    IntermediateOutputPath = Name,
                    CompiledUnits = true,
                    VerifyCompiledUnits = true,
                }).ApplicationModule;
            }
            Assert.True(plain.AsSpan().SequenceEqual(restored), $"{step}: the module differs from a plain compile");
            var reuse = memory.Summary(Name) ?? "";
            if (step != "cold")
            {
                Assert.False(reuse.StartsWith("restored 0 ", StringComparison.Ordinal), $"{step}: nothing was restored: {reuse}");
                Assert.False(reuse.Contains("root maps 0;", StringComparison.Ordinal), $"{step}: no root map was restored: {reuse}");
                // Each step qualifies for the early cutoff, so verification replays every
                // method against the last compile. Only a method emitted afresh may miss: an
                // edited one, or one no unit keeps.
                var replay = System.Text.RegularExpressions.Regex.Match(reuse, @"replayed (\d+), missed (\d+)");
                var emitted = System.Text.RegularExpressions.Regex.Match(reuse, @"emitted (\d+)");
                Assert.True(replay.Success, $"{step}: nothing was replayed: {reuse}");
                Assert.True(int.Parse(replay.Groups[1].Value, CultureInfo.InvariantCulture) > 100, $"{step}: {reuse}");
                Assert.True(int.Parse(replay.Groups[2].Value, CultureInfo.InvariantCulture) <=
                    int.Parse(emitted.Groups[1].Value, CultureInfo.InvariantCulture), $"{step}: {reuse}");
            }
            Assert.NotNull(memory.Inputs(Name));
            // Verification ran the early cutoff too, and its module equals this one.
            var inputs = memory.InputReport(Name) ?? "";
            Assert.Contains(step switch
            {
                "cold" => "early cutoff: declined (no earlier compile)",
                "unchanged" => "early cutoff: nothing changed",
                _ => "early cutoff: patched 1 ",
            }, inputs, StringComparison.Ordinal);
            var carried = new UnitStoreMemory();
            carried.Import(Name, memory.Export(Name));
            memory = carried;
        }

        static IServiceCollection InMemory(UnitStoreMemory memory)
        {
            var services = new ServiceCollection().AddNetWasmCompiler();
            services.Replace(ServiceDescriptor.Singleton(new CompilerParallelism(1)));
            services.UseUnitMemory(memory);
            return services;
        }
    }

    private static void Verify(WasmTarget target, bool stackTrace, (string Step, string Source)[] steps,
        Func<string, bool>? reuses = null)
    {
        using var assets = TestAssets.Create();
        var units = Path.Combine(assets.Directory, "obj");
        var summary = Path.Combine(units, "netwasm-units", "last-compile.txt");

        foreach (var (step, source) in steps)
        {
            var assembly = assets.CompileOptimizedSource("UnitRestore", source);
            var options = new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
                Target: target, EmitStackTrace: stackTrace, Optimization: FinalWasmOptimization.O0);
            var plain = NetWasmCompiler.Compile(options).ApplicationModule;
            var restored = NetWasmCompiler.Compile(options with
            {
                IntermediateOutputPath = units,
                CompiledUnits = true,
                VerifyCompiledUnits = true,
            }).ApplicationModule;

            Assert.True(plain.AsSpan().SequenceEqual(restored), $"{step}: the module differs from a plain compile");
            var reuse = File.ReadAllText(summary);
            if (step == "cold" || reuses?.Invoke(step) == false)
                continue;
            Assert.False(reuse.StartsWith("restored 0 ", StringComparison.Ordinal), $"{step}: nothing was restored: {reuse}");
            Assert.False(reuse.Contains("outlined 0;", StringComparison.Ordinal), $"{step}: nothing was outlined: {reuse}");
            Assert.False(reuse.Contains("root maps 0;", StringComparison.Ordinal), $"{step}: no root map was restored: {reuse}");
        }
    }
}
