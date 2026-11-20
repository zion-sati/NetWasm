// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A program compiled for a collector with generations records its stores of references: it imports the
// runtime's write barrier and tells the link that it does. A program compiled for any other collector, or for
// none by name, is the program it has always been.
public sealed class ReferenceWriteBarrierCompilationTests
{
    private const string Barrier = "record_reference_write";

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AProgramForACollectorWithGenerationsRecordsItsReferenceStores(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ReferenceStores", Source);
        CompilationResult Compile(RuntimeCollector? collector) => NetWasmCompiler.Compile(
            new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target)
            {
                Collector = collector,
            });

        var recording = Compile(RuntimeCollector.NetWasmGen);
        var silent = Compile(null);

        Assert.Contains(Barrier, WasmModuleInspection.ReadFunctionImportNames(recording.ApplicationModule));
        Assert.Contains(NetWasmRuntimeFeatureIds.ReferenceWriteBarrier, recording.RuntimeFeatures);
        Assert.DoesNotContain(Barrier, WasmModuleInspection.ReadFunctionImportNames(silent.ApplicationModule));
        Assert.DoesNotContain(NetWasmRuntimeFeatureIds.ReferenceWriteBarrier, silent.RuntimeFeatures);
        // Nothing else differs between the two programs: the same imports otherwise, and more code.
        Assert.Equal(
            WasmModuleInspection.ReadFunctionImports(silent.ApplicationModule),
            WasmModuleInspection.ReadFunctionImports(recording.ApplicationModule)
                .Where(import => !import.Name.StartsWith(Barrier, StringComparison.Ordinal)));
        Assert.True(recording.ApplicationModule.Length > silent.ApplicationModule.Length);
    }

    [Fact]
    public void AProgramForAnyOtherCollectorIsCompiledAsForNoneByName()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ReferenceStores", Source);
        byte[] Compile(RuntimeCollector? collector) => NetWasmCompiler.Compile(
            new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [])
            {
                Collector = collector,
            }).ApplicationModule;

        var unnamed = Compile(null);

        foreach (var collector in RuntimeCollector.All.Where(collector => !collector.HasGenerations))
            Assert.Equal(unnamed, Compile(collector));
        Assert.NotEqual(unnamed, Compile(RuntimeCollector.NetWasmGen));
    }

    // Every kind of store of a reference into memory: a field, an element, a value that holds a reference into
    // a field, an element and through a ref, a reference through a ref, an exchange, the argument that
    // DynamicInvoke fills in for a null value, a struct result written into a box allocated before the call
    // (DynamicInvoke's and an expression's call), an expression's field assignment, and a block move.
    private const string Source = """
        using System;
        using System.Linq.Expressions;
        using System.Reflection;
        using System.Threading;

        public struct Pair { public object First; public int Second; }
        public sealed class Node { public Node Next; public Pair Pair; public object Slot; }
        public delegate int Twice(int value);

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var node = new Node();
                var other = new Node { Next = node };
                node.Next = other;
                var nodes = new Node[4];
                nodes[1] = node;
                var pairs = new Pair[4];
                var pair = new Pair { First = other, Second = input };
                node.Pair = pair;
                pairs[2] = pair;
                Store(ref pairs[3], pair);
                Store(ref node.Slot, nodes);
                Interlocked.CompareExchange(ref node.Slot, pairs, nodes);
                Twice twice = value => value * 2;
                var arguments = new object[] { null };
                var doubled = (int)twice.DynamicInvoke(arguments);
                Func<int, Pair> maker = MakePair;
                var invoked = (Pair)maker.DynamicInvoke(input)!;
                Expression<Func<Pair>> call = () => MakePair(input + 1);
                var called = call.Compile()();
                Expression<Func<Node, object>> slot = holder => holder.Slot;
                var target = Expression.Parameter(typeof(Node));
                var value = Expression.Parameter(typeof(object));
                Expression.Lambda<Action<Node, object>>(Expression.Assign(
                    Expression.Field(target, (FieldInfo)((MemberExpression)slot.Body).Member), value), target, value)
                    .Compile()(other, invoked.First);
                var copies = new Node[4];
                nodes.AsSpan(0, 2).CopyTo(copies);
                return ReferenceEquals(node.Next.Next, node) && pairs[3].Second == input && node.Slot == pairs && doubled == 0 &&
                    invoked.Second == input && called.Second == input + 1 && other.Slot == invoked.First &&
                    ReferenceEquals(copies[1], node)
                    ? input + 1
                    : -1;
            }

            public static Pair MakePair(int value) => new Pair { First = new Node(), Second = value };

            private static void Store<T>(ref T location, T value) => location = value;
        }
        """;
}
