// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class FixedBufferCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesFixedBufferIndexingAcrossEveryProfile(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        Assert.Equal(48, executor.Execute(new CompilationScenario(
            "FixedBufferFixture",
            Source,
            "FixedBufferFixture.EntryPoint",
            optimize,
            target,
            41,
            [])
        {
            AllowUnsafe = true,
        }));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void FixedBufferDestinationSurvivesAllocatingRightHandSide(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "FixedBufferAllocatingAssignment", """
            public unsafe struct Buffer
            {
                public fixed int Values[8];
            }
            public sealed class Box(int value)
            {
                public int Value = value;
            }
            public static class EntryPoint
            {
                public static unsafe int Run(int input)
                {
                    Buffer buffer = default;
                    buffer.Values[input & 7] = Allocate(input + 1);
                    return buffer.Values[input & 7];
                }
                private static int Allocate(int value) => new Box(value).Value;
            }
            """, "EntryPoint", optimize, target, 41, [])
        { AllowUnsafe = true }));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void FixedBufferInReferenceBearingOwnerSurvivesAllocatingRightHandSide(
        bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "ReferenceBearingFixedBufferAssignment", """
            public unsafe struct Buffer
            {
                public Box Marker;
                public fixed int Values[8];
            }
            public sealed class Box(int value)
            {
                public int Value = value;
            }
            public static class EntryPoint
            {
                public static unsafe int Run(int input)
                {
                    Buffer buffer = default;
                    buffer.Marker = new Box(7);
                    buffer.Values[input & 7] = Allocate(input + 1);
                    return buffer.Values[input & 7] + buffer.Marker.Value - 7;
                }
                private static int Allocate(int value) => new Box(value).Value;
            }
            """, "EntryPoint", optimize, target, 41, [])
        { AllowUnsafe = true }));
    }

    public static TheoryData<bool, WasmTarget, string> ByReferenceReceiverCells
    {
        get
        {
            var cases = new TheoryData<bool, WasmTarget, string>();
            foreach (var optimize in new[] { false, true })
                foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
                    foreach (var receiver in new[] { "Parameter", "Instance", "Return", "Local" })
                    {
                        cases.Add(optimize, target, receiver);
                    }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(ByReferenceReceiverCells))]
    public void FixedBufferByReferenceReceiversSurviveAllocatingRightHandSides(
        bool optimize, WasmTarget target, string receiver)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "ByReferenceFixedBufferAssignment", """
            public unsafe struct Buffer
            {
                public Box Marker;
                public fixed int Values[8];
                public void Set(int index, int value) => Values[index] = EntryPoint.Allocate(value);
            }
            public sealed class Box(int value)
            {
                public int Value = value;
            }
            public static class EntryPoint
            {
                public static unsafe int RunParameter(int input)
                {
                    Buffer buffer = default;
                    buffer.Marker = new Box(7);
                    Set(ref buffer, input & 7, input + 1);
                    return buffer.Values[input & 7] + buffer.Marker.Value - 7;
                }
                public static unsafe int RunInstance(int input)
                {
                    Buffer buffer = default;
                    buffer.Marker = new Box(7);
                    buffer.Set(input & 7, input + 1);
                    return buffer.Values[input & 7] + buffer.Marker.Value - 7;
                }
                public static unsafe int RunReturn(int input)
                {
                    Buffer buffer = default;
                    buffer.Marker = new Box(7);
                    Identity(ref buffer).Values[input & 7] = Allocate(input + 1);
                    return buffer.Values[input & 7] + buffer.Marker.Value - 7;
                }
                public static unsafe int RunLocal(int input)
                {
                    Buffer buffer = default;
                    buffer.Marker = new Box(7);
                    ref var alias = ref Identity(ref buffer);
                    alias.Values[input & 7] = Allocate(input + 1);
                    return alias.Values[input & 7] + buffer.Marker.Value - 7;
                }
                private static unsafe void Set(ref Buffer value, int index, int number) =>
                    value.Values[index] = Allocate(number);
                private static ref Buffer Identity(ref Buffer value) => ref value;
                public static int Allocate(int value) => new Box(value).Value;
            }
            """, "EntryPoint", optimize, target, 41, [])
        {
            AllowUnsafe = true,
            EntryMethodName = "Run" + receiver,
        }));
    }

    private const string Source = """
        namespace FixedBufferFixture;

        public unsafe struct Buffer
        {
            private fixed uint _values[8];

            public void Set(int index, uint value) => _values[index] = value;

            public uint Get(int index) => _values[index];
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Buffer buffer = default;
                buffer.Set(0, 7);
                buffer.Set(7, (uint)input);
                return (int)(buffer.Get(0) + buffer.Get(7));
            }
        }
        """;
}
