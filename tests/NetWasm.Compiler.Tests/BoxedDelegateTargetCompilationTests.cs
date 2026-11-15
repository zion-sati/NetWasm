// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class BoxedDelegateTargetCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void BoxedMethodGroupTargetsRetainIndependentMutableState(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = optimize
            ? assets.CompileOptimizedSource("BoxedDelegateTargets", Source)
            : assets.CompileSource("BoxedDelegateTargets", Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "BoxedDelegateTargets.EntryPoint", "Run", [], target));

        for (var input = 0; input < 7; input++)
        {
            Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
                compilation.ApplicationModule, assets.Directory, input, target, compilation.StaticDataEnd));
        }
    }

    private const string Source = """
        using System;
        namespace BoxedDelegateTargets;

        public interface ICounter { int Next(); }
        public struct Counter
        {
            public int Count;
            public int Next() => ++Count;
        }
        public struct InterfaceCounter : ICounter
        {
            public int Count;
            public int Next() => ++Count;
        }
        public sealed class State { public int Count; }
        public struct WideCounter
        {
            public long Count;
            public State State;
            public long Next() { State.Count++; return ++Count; }
            public void Advance() { Count++; State.Count = (int)Count; }
        }
        public struct GenericCounter<T>
        {
            public T Value;
            public int Count;
            public int Next() => Value == null ? -1 : ++Count;
        }
        public struct Result { public long Value; }
        public struct ResultCounter
        {
            public long Count;
            public Result Next() => new Result { Value = ++Count };
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                switch (input)
                {
                    case 0:
                    {
                        var value = new Counter { Count = 40 };
                        Func<int> next = value.Next;
                        return next() == 41 && next() == 42 && value.Count == 40 ? 42 : -1;
                    }
                    case 1:
                    {
                        var value = new Counter();
                        Func<int> first = value.Next;
                        Func<int> second = value.Next;
                        var alias = first;
                        value.Count = 90;
                        return first() == 1 && alias() == 2 && second() == 1 &&
                            first() == 3 && value.Count == 90 ? 42 : -2;
                    }
                    case 2:
                    {
                        var state = new State();
                        var value = new WideCounter { Count = 0x100000000L, State = state };
                        Func<long> next = value.Next;
                        return next() == 0x100000001L && next() == 0x100000002L &&
                            value.Count == 0x100000000L && state.Count == 2 ? 42 : -3;
                    }
                    case 3:
                    {
                        var value = new GenericCounter<string> { Value = "alive", Count = 40 };
                        Func<int> next = value.Next;
                        return next() == 41 && next() == 42 && value.Count == 40 ? 42 : -4;
                    }
                    case 4:
                    {
                        var value = new ResultCounter { Count = 40 };
                        Func<Result> next = value.Next;
                        return next().Value == 41 && next().Value == 42 && value.Count == 40 ? 42 : -5;
                    }
                    case 5:
                    {
                        var state = new State();
                        var value = new WideCounter { Count = 40, State = state };
                        Action advance = value.Advance;
                        advance();
                        advance();
                        return state.Count == 42 && value.Count == 40 ? 42 : -6;
                    }
                    case 6:
                    {
                        ICounter boxed = new InterfaceCounter { Count = 40 };
                        Func<int> next = boxed.Next;
                        return next() == 41 && next() == 42 && boxed.Next() == 43 ? 42 : -7;
                    }
                    default: return -8;
                }
            }
        }
        """;
}
