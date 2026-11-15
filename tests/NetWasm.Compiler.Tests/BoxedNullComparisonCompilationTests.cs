// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class BoxedNullComparisonCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ClosedValueNullTestsPreserveSemanticsWithoutRetainingBoxes(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Runtime.CompilerServices;
            namespace BoxedNullFixture;
            public enum Kind : short { Ready = 1 }
            public struct Value { public long Number; }
            public static class EntryPoint
            {
                private static int effects;
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static bool Present<T>(T value) => value is not null;
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static bool Absent<T>(T value) => value is null;
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Pick<T>(T value) { if (value is null) return -1; return 7; }
                private static int Effect() { effects++; return 42; }
                private static int Fail() { throw new InvalidOperationException(); }
                public static int Run(int input)
                {
                    effects = 0;
                    if (!Present(Effect()) || effects != 1) return -1;
                    if (Absent(input) || Pick(input) != 7) return -2;
                    if (!Present((long)input) || !Present((double)input)) return -3;
                    if (!Present(Kind.Ready) || !Present(new Value { Number = input })) return -4;
                    if (Present<int?>(null) || !Present<int?>(input)) return -5;
                    if (!Absent<string>(null) || Absent("present")) return -6;
                    try { Present(Fail()); return -7; }
                    catch (InvalidOperationException) { effects++; }
                    finally { effects++; }
                    return effects == 3 ? 42 : -8;
                }
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("BoxedNullFixture", source)
            : assets.CompileSource("BoxedNullFixture", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "BoxedNullFixture.EntryPoint", "Run", [], Target: target));
        var closedTests = result.Program.ConstructedMethods.Values.Where(body =>
            body.Method.DeclaringType.FullName == "BoxedNullFixture.EntryPoint" &&
            body.Method.Definition.Name is "Present" or "Absent" or "Pick").ToArray();
        Assert.NotEmpty(closedTests);
        var nullableTypes = new NullableTypeResolver();
        Assert.DoesNotContain(closedTests.SelectMany(body => body.Body.Instructions), instruction =>
            instruction is { Operation: CilOperation.Box, Operand: CilOperand.TypeIdentity { Value: var type } } &&
            type.IsValueType && nullableTypes.Resolve(type) is null);
        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ClosedValueTypeTestsAndUnboxingKeepSemanticsWithoutBoxing(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System.Runtime.CompilerServices;
            namespace BoxedTypeFixture;
            public enum Kind : int { Ready = 3 }
            public static class EntryPoint
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Low<T>(T value) => value is int number ? number & 7 : -1;
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static bool IsInt<T>(T value) => value is int;
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Unbox<T>(T value) => (int)(object)value!;
                public static int Run(int input)
                {
                    // An int passes; a long, an enum over int and a nullable keep their own answers.
                    if (Low(input) != (input & 7) || Low(5L) != -1 || Low(Kind.Ready) != -1) return -1;
                    if (!IsInt(input) || IsInt((short)input) || IsInt<int?>(null) || !IsInt<int?>(input)) return -2;
                    // Unboxing accepts an enum as its underlying type, unlike a type test.
                    if (Unbox(input) != input || Unbox(Kind.Ready) != 3) return -3;
                    try { Unbox(5L); return -4; }
                    catch (System.InvalidCastException) { }
                    return 42;
                }
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("BoxedTypeFixture", source)
            : assets.CompileSource("BoxedTypeFixture", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "BoxedTypeFixture.EntryPoint", "Run", [], Target: target));
        // The int instantiations no longer box.
        var integers = result.Program.ConstructedMethods.Values.Where(body =>
            body.Method.DeclaringType.FullName == "BoxedTypeFixture.EntryPoint" &&
            body.Method.MethodArguments is [{ CanonicalName: "primitive:i4" }]).ToArray();
        Assert.Equal(3, integers.Length);
        Assert.DoesNotContain(integers.SelectMany(body => body.Body.Instructions), static instruction => instruction.Operation == CilOperation.Box);
        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }
}
