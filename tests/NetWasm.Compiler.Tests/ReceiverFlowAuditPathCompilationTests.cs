// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// Paths by which the closure reaches a method that the receiver flow must follow too, each compiled under the receiver
// flow audit, which fails a build whose closure compiled a method the flow did not reach.
public sealed class ReceiverFlowAuditPathCompilationTests
{
    // An interface method called on a struct type argument (a constrained call the closure resolves to the struct's
    // own method), as Base64's decoders are; and a static abstract member called on a type argument.
    private const string ConstrainedStruct = """
        public interface IStep { int Next(int value); }
        public interface IStart { static abstract int Start(int value); }
        public struct Twice : IStep, IStart { public int Next(int value) => value * 2; public static int Start(int value) => value + 100; }
        public struct AddThree : IStep, IStart { public int Next(int value) => value + 3; public static int Start(int value) => value + 200; }

        public static class EntryPoint
        {
            public static int Run(int input) => Apply(new Twice(), input) + Apply(new AddThree(), input) * 10 +
                Begin<Twice>(input) * 1000 + Begin<AddThree>(input) * 100000;

            private static int Apply<T>(T step, int value) where T : IStep => step.Next(value);

            private static int Begin<T>(int value) where T : IStart => T.Start(value);
        }
        """;

    // A final method (string.Equals(string) implements IEquatable<string>) waits for no flow, so the closure compiles
    // it as soon as it reads the call; the receiver comes from a virtual property the flow releases a round later.
    private const string AheadOfTheFlow = """
        public abstract class Shape { public abstract string Label { get; } }
        public sealed class Square : Shape { public override string Label => "square"; }

        public static class EntryPoint
        {
            private static readonly Shape Value = new Square();

            public static int Run(int input) => Value.Label.Equals("square") ? 42 + input : input;
        }
        """;

    // Marshal.PtrToStructure by runtime type runs a class's parameterless constructor itself, as no IL calls it.
    private const string MarshalledConstructor = """
        using System;
        using System.Runtime.InteropServices;

        [StructLayout(LayoutKind.Sequential)]
        public sealed class Pair
        {
            public int Left;
            public int Right;
            public Pair() { Left = 5; }
        }

        public static class EntryPoint
        {
            public static unsafe int Run(int input)
            {
                var values = stackalloc int[2];
                values[0] = input;
                values[1] = 30;
                var pair = (Pair)Marshal.PtrToStructure((IntPtr)values, typeof(Pair))!;
                return pair.Left + pair.Right;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void ConstrainedCallsOnStructTypeArgumentsAreFollowed(FinalWasmOptimization optimization) =>
        Assert.Equal(14 + 100 + 107000 + 20700000, Run("ReceiverFlowConstrainedStructFixture", ConstrainedStruct,
            optimization, 7));

    [Theory]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void ACalleeTheClosureCompilesAheadOfTheFlowIsReachedOnceItCatchesUp(FinalWasmOptimization optimization) =>
        Assert.Equal(49, Run("ReceiverFlowAheadFixture", AheadOfTheFlow, optimization, 7));

    [Theory]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void ConstructorsTheMarshallerRunsAreBoundaries(FinalWasmOptimization optimization) =>
        Assert.Equal(37, Run("ReceiverFlowMarshalledFixture", MarshalledConstructor, optimization, 7, allowUnsafe: true));

    private static int Run(string name, string source, FinalWasmOptimization optimization, int input,
        bool allowUnsafe = false)
    {
        using var assets = TestAssets.Create();
        var assembly = allowUnsafe ? assets.CompileUnsafeSource(name, source) : assets.CompileSource(name, source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization)
        {
            ReceiverFlowAudit = true,
        });
        return ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, WasmTarget.Wasm32,
            result.StaticDataEnd);
    }
}
