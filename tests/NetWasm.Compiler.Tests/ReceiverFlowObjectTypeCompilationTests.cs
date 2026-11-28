// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// A constrained GetType on a type argument becomes GetObjectType of that type, and the call after it (ToString on the
// Type object, as System.Collections.Immutable's Requires.FailObjectDisposed makes) dispatches on the Type object, not
// the constrained type: with the prefix left in place, the call took the struct or class as its receiver bound, had
// no target for the Type object, and threw InvalidCastException where it should name the disposed type.
public sealed class ReceiverFlowObjectTypeCompilationTests
{
    internal const string Source = """
        using System;

        public struct Enumerator { public int X; }
        public sealed class Box { public int Y; }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var total = 0;
                try { Fail(new Enumerator { X = input }); }
                catch (ObjectDisposedException exception) { total += exception.ObjectName == typeof(Enumerator).ToString() ? 1 : 2; }
                try { Fail(new Box { Y = input }); }
                catch (ObjectDisposedException exception) { total += exception.ObjectName == typeof(Box).ToString() ? 10 : 20; }
                return total;
            }

            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            private static void Fail<T>(T disposed) => throw new ObjectDisposedException(disposed!.GetType().ToString());
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void ATypeObjectTheProgramObtainsDispatchesItsCalls(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowObjectTypeFixture", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization));

        Assert.Equal(11, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3,
            WasmTarget.Wasm32, result.StaticDataEnd));
    }
}
