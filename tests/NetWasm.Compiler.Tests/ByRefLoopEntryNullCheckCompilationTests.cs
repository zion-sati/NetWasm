// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Loops that read or write through a managed-address argument, from a null one: each throws NullReferenceException
// exactly where its first access is at address 0, as O0 and Mono AOT do, and a store the body makes before that access
// is still seen; a loop that never runs does not throw. A condition that reads through the address throws from the
// condition, a cursor the body moves throws at its first read, and so does a loop whose first read is in an if's test.
// The shapes a loop-entry check would take, which must not move the throw.
public sealed class ByRefLoopEntryNullCheckCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading;

        public static class Loops
        {
            public static int Stores;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Masked(ref int first, int count)
            {
                var total = 0;
                for (var index = 0; index < count; index++) total += Unsafe.Add(ref first, index & 3);
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int StoreFirst(ref int first, int count)
            {
                var total = 0;
                for (var index = 0; index < count; index++)
                {
                    Stores++;
                    total += Unsafe.Add(ref first, index & 1);
                }
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Condition(ref int first, int count)
            {
                var index = 0;
                while (Unsafe.Add(ref first, index & 1) != count) index++;
                return index;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Walked(ref int first, int count)
            {
                var total = 0;
                for (var index = 0; index < count; index++)
                {
                    total += first;
                    first = ref Unsafe.Add(ref first, 1);
                }
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Exchanged(ref int first, int count)
            {
                var total = 0;
                for (var index = 0; index < count; index++)
                {
                    total += Interlocked.CompareExchange(ref Unsafe.Add(ref first, index & 3), index, -1);
                }
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Swapped(ref int first, ref int second, int count)
            {
                var total = 0;
                for (var index = 0; index < count; index++)
                {
                    total += first;
                    first = ref second;
                }
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Found(ref int first, int count, int value)
            {
                for (var index = 0; index < count; index++)
                {
                    if (Unsafe.Add(ref first, index) == value) return index;
                }
                return -1;
            }

            // A local's address is never null.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Local(int value)
            {
                var location = value;
                return Interlocked.CompareExchange(ref location, value + 1, value) + location;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Nested(ref int first, int rows, int columns)
            {
                var total = 0;
                for (var row = 0; row < rows; row++)
                {
                    for (var column = 0; column < columns; column++) total += Unsafe.Add(ref first, (row ^ column) & 3);
                }
                return total;
            }
        }

        public static class EntryPoint
        {
            private static int Caught(Func<int> run)
            {
                try
                {
                    return run();
                }
                catch (NullReferenceException)
                {
                    return -1000;
                }
            }

            public static int Run(int input)
            {
                var values = new int[] { 1, 2, 3, input };
                if (Loops.Masked(ref values[0], 6) != 1 + 2 + 3 + 42 + 1 + 2) return 1;
                if (Caught(() => Loops.Masked(ref Unsafe.NullRef<int>(), 0)) != 0) return 2;
                if (Caught(() => Loops.Masked(ref Unsafe.NullRef<int>(), 3)) != -1000) return 3;
                Loops.Stores = 0;
                if (Caught(() => Loops.StoreFirst(ref Unsafe.NullRef<int>(), 3)) != -1000 || Loops.Stores != 1) return 4;
                if (Caught(() => Loops.Condition(ref Unsafe.NullRef<int>(), 7)) != -1000) return 5;
                if (Loops.Condition(ref values[0], 2) != 1) return 6;
                if (Caught(() => Loops.Walked(ref Unsafe.NullRef<int>(), 2)) != -1000) return 7;
                if (Loops.Walked(ref values[0], 4) != 1 + 2 + 3 + 42) return 8;
                if (Caught(() => Loops.Nested(ref Unsafe.NullRef<int>(), 3, 0)) != 0) return 9;
                if (Caught(() => Loops.Nested(ref Unsafe.NullRef<int>(), 2, 2)) != -1000) return 10;
                if (Loops.Nested(ref values[0], 2, 2) != 1 + 2 + 2 + 1) return 11;
                if (Caught(() => Loops.Exchanged(ref Unsafe.NullRef<int>(), 0)) != 0) return 12;
                if (Caught(() => Loops.Exchanged(ref Unsafe.NullRef<int>(), 2)) != -1000) return 13;
                values[1] = -1;
                if (Loops.Exchanged(ref values[0], 3) != 1 - 1 + 3 || values[1] != 1) return 14;
                if (Loops.Local(input) != 2 * input + 1) return 15;
                if (Caught(() => Loops.Swapped(ref values[0], ref Unsafe.NullRef<int>(), 1)) != 1) return 16;
                if (Caught(() => Loops.Swapped(ref values[0], ref Unsafe.NullRef<int>(), 2)) != -1000) return 17;
                if (Caught(() => Loops.Found(ref Unsafe.NullRef<int>(), 0, 3)) != -1) return 18;
                if (Caught(() => Loops.Found(ref Unsafe.NullRef<int>(), 2, 3)) != -1000) return 19;
                if (Loops.Found(ref values[0], 4, 3) != 2 || Loops.Found(ref values[0], 4, 5) != -1) return 20;
                return 42;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.O1, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm64)]
    public void NullAddressesThrowWhereDesktopThrows(FinalWasmOptimization optimization, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ByRefLoopEntryChecks", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));

        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 42, target, result.StaticDataEnd));
    }
}
