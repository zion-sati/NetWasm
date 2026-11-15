// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TreeTraversalCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, true, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, false, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, true, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, false, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, true, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, false, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, true, FinalWasmOptimization.O3)]
    public void SharedLoopExitEffectsRunExactlyOnceOnTheSelectedPath(
        WasmTarget target, bool optimize, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "SharedLoopExit", SharedExitSource, "SharedLoopExit.EntryPoint", optimize, target, 41, [])
        { Optimization = optimization }));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void KeepsConditionTransfersAndFinallyCleanupDistinctFromHeaderTransfers(
        WasmTarget target, bool optimize)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "LoopTransfers", LoopSource, "LoopTransfers.EntryPoint", optimize, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void VisitsChildBeforeParentContinuation(WasmTarget target, bool optimize)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "TreeTraversal", Source, "TreeTraversal.EntryPoint", optimize, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string SharedExitSource = """
        namespace SharedLoopExit;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var expected = new[] { 1100, 1100, 1010, 1010, 1106, 1103, 1016, 1103 };
                for (var index = 0; index < expected.Length; index++)
                {
                    if (Visit(index < 4 ? 0 : 3, index & 3) != expected[index]) return 1;
                }
                return 42;
            }

            private static int Visit(int limit, int mode)
            {
                var count = 0;
                var total = 0;
                while (count < limit)
                {
                    count++;
                    total += count;
                    if ((mode & 1) != 0 && count == 2) goto Shared;
                }
                if ((mode & 2) != 0)
                {
                    total += 10;
                    goto Done;
                }
            Shared:
                total += 100;
            Done:
                total += 1000;
                return total;
            }
        }
        """;

    private const string LoopSource = """
        namespace LoopTransfers;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var iteration = 0;
                var checks = 0;
                var total = 0;
                do
                {
                    iteration++;
                    if (iteration < 3) continue;
                    total += iteration;
                } while (++checks < 4);
                if (checks != 4 || iteration != 4 || total != 7) return 1;

                var count = 0;
                while (true)
                {
                    count++;
                    if (count < 3) continue;
                    break;
                }
                if (count != 3) return 2;

                var phase = 0;
                var cleanup = 0;
                do
                {
                    try
                    {
                        phase++;
                        if (phase < 3) continue;
                    }
                    finally
                    {
                        cleanup++;
                    }
                } while (phase < 4);
                return phase == 4 && cleanup == 4 ? 42 : 3;
            }
        }
        """;

    private const string Source = """
        using System;
        namespace TreeTraversal;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var root = new Node();
                root.ChildNode = new Node { Parent = root, Code = 1 };
                using var writer = new Writer(root, stackalloc int[32]);
                var result = writer.EmitCode();
                return result == 111 ? 42 : result;
            }
        }
        public sealed class Node
        {
            public Node Parent;
            public Node ChildNode;
            public int Code;
            public int ChildCount() => ChildNode is null ? 0 : 1;
            public Node Child(int index) => ChildNode;
        }
        public ref struct Writer
        {
            private readonly Node _root;
            private Span<int> _stack;
            private int _length;
            public Writer(Node root, Span<int> stack)
            {
                _root = root;
                _stack = stack;
                _length = 0;
            }
            public void Dispose() { }
            public int EmitCode()
            {
                var current = _root;
                var child = 0;
                var result = 0;
                while (true)
                {
                    var count = current.ChildCount();
                    if (count == 0)
                    {
                        result += current.Code;
                    }
                    else if (child < count)
                    {
                        result += 10;
                        current = current.Child(child);
                        _stack[_length++] = child;
                        child = 0;
                        continue;
                    }
                    if (_length == 0) break;
                    child = _stack[--_length];
                    current = current.Parent;
                    result += 100;
                    child++;
                }
                return result;
            }
        }
        """;
}
