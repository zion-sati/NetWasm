using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Wasm.Units;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Units;

// A unit keeps every query its method's emission asked, so a later compile can tell whether
// each answer still holds. This program asks every kind of query a body can ask.
public sealed class UnitQueryKindTests
{
    internal const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        public enum Color { Red = 1, Green = 2, Blue = 4 }

        [Flags]
        public enum Access : byte { None = 0, Read = 1, Write = 2 }

        public struct Pair
        {
            public int Left;
            public long Right;
            public Pair(int left, long right) { Left = left; Right = right; }
        }

        public struct Holder
        {
            public string Text;
            public int Count;
        }

        public interface IShape { int Area(); }
        public sealed class Square(int side) : IShape { public int Area() => side * side; }
        public sealed class Rect(int w, int h) : IShape { public int Area() => w * h; }

        public abstract class Visitor { public abstract T Visit<T>(T value); }
        public sealed class EchoVisitor : Visitor { public override T Visit<T>(T value) => value; }

        public sealed class Resource
        {
            public static int Finalized;
            ~Resource() { Finalized++; }
        }

        public static class Settings
        {
            public static readonly int[] Primes = [2, 3, 5, 7, 11, 13];
            public static readonly string Name;
            static Settings() { Name = "settings:" + Primes.Length; }
        }

        public static class Switches
        {
            public static bool Quiet = true;
        }

        public static class Calls
        {
            public static int Twice(int value) => value * 2;
            public static void Swap(ref int a, ref int b) { (a, b) = (b, a); }
            public static int Read(in Pair pair) => pair.Left;
        }

        public static unsafe class EntryPoint
        {
            private static ReadOnlySpan<byte> Bytes => [1, 2, 3, 4, 5, 6, 7, 8];

            public static int Run(int input) =>
                Dispatch(input) + Boxes(input) + Arrays(input) + Enums(input) + Types(input) + Statics() +
                References() + Delegates(input) + Pointers(input) + Exceptions(input) + Filtered(input) +
                Quietly(input) + Anchored(input) + Resources() + RuntimeData(input);

            private static int Dispatch(int input)
            {
                var total = 0;
                var shapes = new List<IShape> { new Square(input), new Rect(input, 3) };
                foreach (var shape in shapes) total += shape.Area();
                Visitor visitor = new EchoVisitor();
                return total + visitor.Visit(input) + visitor.Visit("text").Length;
            }

            private static int Boxes(int input)
            {
                object boxed = new Pair(input, 7);
                var total = boxed is IShape ? 1 : 0;
                if (boxed is Pair pair) total += pair.Left + Calls.Read(in pair);
                return total;
            }

            private static int Arrays(int input)
            {
                var objects = new object[] { "a", input };
                var grid = new int[2, 3];
                grid[1, 2] = input;
                return grid[1, 2] + objects.Length;
            }

            private static int Enums(int input) =>
                ((Color)input).ToString().Length + Enum.GetNames<Color>().Length +
                (Access.Read | Access.Write).ToString().Length + Enum.ToObject(typeof(Color), 2).GetHashCode();

            private static int Types(int input) =>
                (Nullable.GetUnderlyingType(typeof(int?)) == typeof(int) ? 1 : 0) +
                typeof(Pair).Name.Length + ((object)input).GetType().Name.Length +
                typeof(Dictionary<,>).GetGenericArguments().Length;

            private static int Statics() => Settings.Name.Length + Settings.Primes[2] + Bytes[3];

            private static int References()
            {
                int a = 1, b = 2;
                Calls.Swap(ref a, ref b);
                return a;
            }

            private static int Delegates(int input)
            {
                Func<int, int> twice = Calls.Twice;
                Func<int, int> add = value => value + input;
                Func<int, int> combined = (Func<int, int>)Delegate.Combine(twice, add);
                return twice(input) + (int)twice.DynamicInvoke(3)! + combined(1);
            }

            private static int Pointers(int input)
            {
                delegate*<int, int> pointer = &Calls.Twice;
                return pointer(input);
            }

            private static int Exceptions(int input)
            {
                try
                {
                    if (input < 0) throw new ArgumentException("negative");
                    return checked(input * 1000000);
                }
                catch (ArgumentException exception)
                {
                    return exception.StackTrace?.Length ?? 0;
                }
                catch (OverflowException)
                {
                    return -1;
                }
            }

            private static int Filtered(int input)
            {
                var objects = new object[] { "a" };
                try
                {
                    return objects[input].GetHashCode();
                }
                catch (IndexOutOfRangeException) when (input > 2)
                {
                    return -2;
                }
            }

            // A filter that captures nothing shares no environment, so its method is kept.
            private static int Quietly(int input)
            {
                var objects = new object[] { "a" };
                try
                {
                    return objects[input].GetHashCode();
                }
                catch (IndexOutOfRangeException) when (Switches.Quiet)
                {
                    return -3;
                }
            }

            // A reference into a struct that holds a reference lives across an allocation.
            private static int Anchored(int input)
            {
                var holder = new Holder { Text = "anchored", Count = input };
                ref var count = ref holder.Count;
                Resource.Finalized += new object().GetHashCode();
                count++;
                return count + holder.Text.Length;
            }

            private static int Resources()
            {
                _ = new Resource();
                return Resource.Finalized;
            }

            // GetUninitializedObject and Marshal's structure members dispatch over the program's
            // allocated and marshalled types.
            private static int RuntimeData(int input) =>
                (RuntimeHelpers.GetUninitializedObject(typeof(Square)) is Square ? 1 : 0) +
                Marshal.SizeOf((object)new Pair(input, 1));
        }
        """;

    // One body changes; the rest are restored and replayed against the new compile.
    private static readonly string BodyEdit = Source.Replace("value * 2", "value * 4", StringComparison.Ordinal);

    // Layouts, type ids, enum metadata, static data and strings move, so the answers the
    // kept units recorded no longer hold everywhere.
    private static readonly string ShapeEdit = Source
        .Replace("public int Left;", "public byte Tag;\n    public int Left;", StringComparison.Ordinal)
        .Replace("Blue = 4 }", "Blue = 4, Black = 8 }", StringComparison.Ordinal)
        .Replace("public static readonly string Name;", "public static readonly string Name;\n    public static long Created;",
            StringComparison.Ordinal)
        .Replace("\"settings:\"", "\"settings, moved:\"", StringComparison.Ordinal)
        .Replace("public sealed class Rect(int w, int h) : IShape { public int Area() => w * h; }",
            "public sealed class Rect(int w, int h) : IShape { public int Area() => w * h; }\n" +
            "public sealed class Circle(int r) : IShape { public int Area() => 3 * r * r; }", StringComparison.Ordinal)
        .Replace("new Rect(input, 3) };", "new Rect(input, 3), new Circle(input) };", StringComparison.Ordinal);

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EveryKindReplaysAcrossEditsAsAPlainCompileWould(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var units = Path.Combine(assets.Directory, "obj");
        var summary = Path.Combine(units, "netwasm-units", "last-compile.txt");
        foreach (var (step, source) in new[]
                 {
                     ("cold", Source), ("unchanged", Source), ("body", BodyEdit), ("shape", ShapeEdit), ("back", Source),
                 })
        {
            var options = new CompilerOptions(assets.CompileOptimizedUnsafeSource("UnitKinds", source), [assets.CoreLib],
                "EntryPoint", "Run", [], Target: target, EmitStackTrace: true, Optimization: FinalWasmOptimization.O0);
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

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EveryKindABodyAsksIsKeptAndReadsBack(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var handles = Path.Combine(assets.Directory, "MemberHandles.dll");
        File.WriteAllBytes(handles, [.. new Correctness.MemberHandleFixtureBuilder().Build()]);

        // Field and method handles come from ldtoken, which C# emits for neither.
        var asked = Asked(assets, assets.CompileOptimizedUnsafeSource("UnitKinds", Source), "EntryPoint", target);
        asked.UnionWith(Asked(assets, handles, "MemberHandles.EntryPoint", target));
        asked.UnionWith(Asked(assets, assets.CompileOptimizedSource("UnitBase", CompiledUnitRestoreTests.BaseSource),
            "EntryPoint", target));

        var missing = Enum.GetValues<UnitQueryKind>().Where(kind => !asked.Contains(kind) && !Unasked.Contains(kind)).ToArray();
        Assert.True(missing.Length == 0, "never asked: " + string.Join(", ", missing));
        Assert.False(asked.Overlaps(Unasked), "asked a kind listed as unasked: " + string.Join(", ", asked.Intersect(Unasked)));
    }

    // Kinds no body of these programs asks: the whole program's static data, root addresses
    // and data segments, which module code reads; a method's
    // root map, which only a filter funclet reads, of a method kept only when its filters
    // capture nothing; and a definition's object layout, which a construction asks only when
    // it names its constructor by definition. A unit still records them when a body asks.
    private static readonly HashSet<UnitQueryKind> Unasked =
    [
        UnitQueryKind.StaticDataEnd, UnitQueryKind.StaticRootAddresses, UnitQueryKind.DataSegments,
        UnitQueryKind.RootMap, UnitQueryKind.ObjectLayoutOfDefinition,
    ];

    // The answers no body asks here, as a compile gives them, read back as they were written.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void UnaskedKindsReadBackAsTheyWereWritten(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var options = new CompilerOptions(assets.CompileOptimizedUnsafeSource("UnitKinds", Source), [assets.CoreLib],
            "EntryPoint", "Run", [], Target: target, Optimization: FinalWasmOptimization.O0);
        var result = NetWasmCompiler.Compile(options);
        using var provider = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        using var lease = provider.GetRequiredService<IMetadataCompilationLoader>().Load(options.EntryAssemblyPath,
            options.ReferencePaths);
        var methods = provider.GetRequiredService<IMethodRepositoryFactory>().Create(lease.Snapshot);
        var fields = provider.GetRequiredService<IFieldRepositoryFactory>().Create(lease.Snapshot);
        var layouts = result.Layouts;
        // A root map anchoring stack storage, as a body holding an interior pointer across a
        // safepoint gets, with every kind of anchor assignment.
        var backing = layouts.ValueLayouts.Keys.First(type => type.CanonicalName.EndsWith("Holder", StringComparison.Ordinal));
        var rootMap = result.Program.RootMaps.Values.MaxBy(map => map.Safepoints.Count)! with
        {
            AnchorPlan = new(
                [new(new(StackStorageLocationKind.Local, 0), backing), new(new(StackStorageLocationKind.EvaluationStack, 1), backing)],
                ImmutableDictionary<int, ImmutableArray<StackStorageAnchorAssignment>>.Empty.Add(4,
                [
                    new StackStorageAnchorAssignment.Capture(0, 1), new StackStorageAnchorAssignment.Copy(1, 0),
                    new StackStorageAnchorAssignment.Clear(0),
                ])),
        };
        Assert.NotEmpty(layouts.StaticRootAddresses);
        Assert.NotEmpty(layouts.DataSegments);
        (UnitQueryKind Kind, object Value)[] answers =
        [
            (UnitQueryKind.StaticDataEnd, layouts.StaticDataEnd),
            (UnitQueryKind.StaticRootAddresses, layouts.StaticRootAddresses),
            (UnitQueryKind.DataSegments, layouts.DataSegments),
            (UnitQueryKind.RootMap, new UnitLookup<MethodRootMap>(true, rootMap)),
            (UnitQueryKind.RootMap, new UnitLookup<MethodRootMap>(false, null)),
            (UnitQueryKind.ObjectLayoutOfDefinition, layouts.ObjectLayouts.Values.First()),
        ];
        Assert.Equal(Unasked, answers.Select(answer => answer.Kind).ToHashSet());
        foreach (var (kind, value) in answers)
        {
            var bytes = UnitAnswerCodec.Encode(kind, "", value);
            Assert.NotNull(bytes);
            Assert.Equal(bytes, UnitAnswerCodec.Encode(kind, "", UnitAnswerCodec.Decode(kind, "", bytes, methods, fields)));
        }
    }

    // The kinds a compile's units asked; every value kept reads back as it was written.
    private static HashSet<UnitQueryKind> Asked(TestAssets assets, string assembly, string entryType, WasmTarget target)
    {
        const string Name = "kinds";
        var memory = new UnitStoreMemory();
        var options = new CompilerOptions(assembly, [assets.CoreLib], entryType, "Run", [], Target: target,
            EmitStackTrace: true, Optimization: FinalWasmOptimization.O0,
            ReferenceAssemblyAliases: ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib"))
        {
            IntermediateOutputPath = Name,
            CompiledUnits = true,
        };
        using var provider = new ServiceCollection().AddNetWasmCompiler().UseUnitMemory(memory).BuildServiceProvider();
        provider.GetRequiredService<INetWasmCompiler>().Compile(options);

        using var lease = provider.GetRequiredService<IMetadataCompilationLoader>().Load(options.EntryAssemblyPath,
            options.ReferencePaths, options.ReferenceAssemblyAliases);
        var methods = provider.GetRequiredService<IMethodRepositoryFactory>().Create(lease.Snapshot);
        var fields = provider.GetRequiredService<IFieldRepositoryFactory>().Create(lease.Snapshot);
        using var store = new MemoryUnitStoreLoader(memory).Load(Name);
        var asked = new HashSet<UnitQueryKind>();
        foreach (var segment in store.Segments)
        {
            if (segment is null) continue;
            for (var id = 0; id < segment.QueryCount; id++)
            {
                var query = segment.Query(id);
                asked.Add(query.Kind);
                if (query.Value.IsEmpty) continue;
                var key = query.Key.ToString();
                var value = UnitAnswerCodec.Decode(query.Kind, key, query.Value, methods, fields);
                var again = UnitAnswerCodec.Encode(query.Kind, key, value);
                Assert.True(again is not null && query.Value.SequenceEqual(again), $"{query.Kind} '{key}' reads back differently");
            }
        }
        return asked;
    }
}
