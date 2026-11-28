// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Field reads around protected blocks, which FieldLoadReuser reuses where every path read the field since its last
// change: a finally that is not copied changes the field through a call on the way out of its try (a store the cached
// value cannot follow), a catch reads what the try stored before it threw, and a filter reads it too. Run's checksum is
// desktop's.
public sealed class LoadsAroundProtectedBlocksCompilationTests
{
    private const int DesktopChecksum = 666434197;

    public static TheoryData<bool, FinalWasmOptimization> Cases()
    {
        var cases = new TheoryData<bool, FinalWasmOptimization>();
        foreach (var optimization in new[]
            { FinalWasmOptimization.None, FinalWasmOptimization.O1, FinalWasmOptimization.O3, FinalWasmOptimization.Oz })
        {
            cases.Add(false, optimization);
            cases.Add(true, optimization);
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReadsSeeEveryStoreOnTheirPath(bool optimize, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            $"LoadsAroundProtectedBlocks{optimization}{(optimize ? "Release" : "Debug")}",
            Source,
            "LoadsAroundProtectedBlocks.EntryPoint",
            optimize,
            WasmTarget.Wasm32,
            41,
            [])
        {
            Optimization = optimization,
        });

        Assert.Equal(DesktopChecksum, result);
    }

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace LoadsAroundProtectedBlocks;

        public sealed class Node
        {
            public int X;
        }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            static void Fail(Node node)
            {
                if (node.X > 100) throw new InvalidOperationException();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            static void Bump(Node node)
            {
                node.X = node.X * 2 + 1;
                Fail(node);
            }

            // A leave out of a finally that is not copied (its handler holds a try): the call in the finally changes the field,
            // and the read after it must see the change.
            [MethodImpl(MethodImplOptions.NoInlining)]
            static int FinallyStore(Node a)
            {
                var r = a.X;
                try { r++; }
                finally
                {
                    try { Bump(a); }
                    catch (InvalidOperationException) { a.X++; }
                }
                return r + a.X;
            }

            // A catch reads the field the try stored before it threw.
            [MethodImpl(MethodImplOptions.NoInlining)]
            static int CatchSeesStore(Node a)
            {
                var r = a.X;
                try { a.X = 500; Fail(a); r += a.X; }
                catch (InvalidOperationException) { return r + a.X; }
                return r + a.X;
            }

            // A read in the try reuses the one before it; the filter reads the field the try stored.
            [MethodImpl(MethodImplOptions.NoInlining)]
            static int FilterReads(Node a)
            {
                var r = a.X;
                try { r += a.X; a.X = 300; Fail(a); }
                catch (InvalidOperationException) when (a.X + a.X == 600) { return r - a.X; }
                return r + a.X;
            }

            public static int Run(int input)
            {
                var total = 0;
                foreach (var x in new[] { 0, 1, input, 101 })
                {
                    total = unchecked(total * 31 + FinallyStore(new Node { X = x }));
                    total = unchecked(total * 31 + CatchSeesStore(new Node { X = x }));
                    total = unchecked(total * 31 + FilterReads(new Node { X = x }));
                }
                return total;
            }
        }
        """;
}
