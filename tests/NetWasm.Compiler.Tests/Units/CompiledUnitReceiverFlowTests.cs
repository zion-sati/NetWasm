// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Units;

// A generic virtual call dispatches on the receivers the whole program's flow brings it, so an unchanged method's code
// depends on other methods: an edit elsewhere that brings a type the program already has to its call recompiles its
// unit (no type id or method moves, so only the call's targets say so), and the kept compile writes what a clean
// compile writes.
public sealed class CompiledUnitReceiverFlowTests
{
    private const string Library = """
        namespace Flow;
        public abstract class Seq<T>
        {
            public abstract Seq<R> Map<R>(System.Func<T, R> map);
            public abstract int Count();
        }
        public sealed class ArraySeq<T>(T[] items) : Seq<T>
        {
            public override Seq<R> Map<R>(System.Func<T, R> map) => new MappedSeq<T, R>(this, map);
            public override int Count() => items.Length;
        }
        public sealed class ListSeq<T>(System.Collections.Generic.List<T> items) : Seq<T>
        {
            public override Seq<R> Map<R>(System.Func<T, R> map) => new MappedSeq<T, R>(this, map);
            public override int Count() => items.Count;
        }
        public sealed class MappedSeq<S, T>(Seq<S> source, System.Func<S, T> map) : Seq<T>
        {
            public override Seq<R> Map<R>(System.Func<T, R> next) => new MappedSeq<S, R>(source, value => next(map(value)));
            public override int Count() => source.Count();
        }
        public static class Store { public static Seq<int> Value = new ArraySeq<int>([1, 2]); }
        public static class Unchanged { public static int Measure() => Store.Value.Map(value => value.ToString()).Count(); }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void AnEditThatBringsANewReceiverRecompilesTheUnchangedCall(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        // The unchanged method is in a library the edit does not touch, so its unit can be restored.
        var library = assets.CompileSource("Flow.Library", Library);
        // Both versions construct a ListSeq<int> and map it to string elsewhere, so the program's types and methods are
        // the same; only the second brings the ListSeq to the unchanged method's call.
        var before = assets.CompileSource("Flow.App", """
            namespace Flow;
            public static class Entry
            {
                private static readonly Seq<int> Other = new ListSeq<int>([5, 6, 7]);
                public static int Run(int input) => Unchanged.Measure() + Other.Map(value => value.ToString()).Count();
            }
            """, library);
        var options = new CompilerOptions(before, [assets.CoreLib, library], "Flow.Entry", "Run", [], WasmTarget.Wasm32,
            Optimization: optimization)
        {
            IntermediateOutputPath = Path.Combine(assets.Directory, "obj"),
            CompiledUnits = true,
        };
        Assert.NotEmpty(NetWasmCompiler.Compile(options).ApplicationModule);

        var after = assets.CompileSource("Flow.App", """
            namespace Flow;
            public static class Entry
            {
                private static readonly Seq<int> Other = new ListSeq<int>([5, 6, 7]);
                public static int Run(int input)
                {
                    if (input > 1) Store.Value = Other;
                    return Unchanged.Measure() + Other.Map(value => value.ToString()).Count();
                }
            }
            """, library);
        var kept = NetWasmCompiler.Compile(options with { EntryAssemblyPath = after });
        // The kept compile restores the units the edit left valid, so the check is not a clean compile twice.
        var summary = File.ReadAllText(Directory.EnumerateFiles(Path.Combine(assets.Directory, "obj"), "last-compile.txt",
            SearchOption.AllDirectories).Single());
        var restored = int.Parse(System.Text.RegularExpressions.Regex.Match(summary, @"restored (\d+)").Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(restored > 0, summary);
        var clean = NetWasmCompiler.Compile(options with
        {
            EntryAssemblyPath = after,
            IntermediateOutputPath = Path.Combine(assets.Directory, "clean"),
        });

        Assert.True(kept.ApplicationModule.AsSpan().SequenceEqual(clean.ApplicationModule),
            "the kept compile reused a unit whose call lost the new receiver");
    }
}
