// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class NullableCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesConstructedGenericNullableThroughInParameters(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            namespace GenericNullableInFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int? empty = null;
                    int? present = input;
                    int? conditional = input % 2 != 0 ? input : null;
                    string? missingText = null;
                    if (!Probe<string?>.WriteCore(in missingText)) return 9;
                    var classProbe = new GenericNullProbe<string?>();
                    if (!classProbe.Add(missingText)) return 10;
                    Converter<string> textConverter = new TextConverter();
                    var textState = new WriteState();
                    if (!textConverter.WriteCore(
                            new object(), "value", new object(), ref textState))
                        return 6;
                    Converter<int> intConverter = new IntConverter();
                    var intState = new WriteState();
                    if (!intConverter.WriteCore(
                            new object(), input, new object(), ref intState))
                        return 7;
                    if (!Probe<int?>.WriteCore(in empty)) return 1;
                    if (Probe<int?>.WriteCore(in present)) return 2;
                    Converter<int?> converter = new NullableConverter<int>();
                    var state = new WriteState { Marker = 7 };
                    if (!converter.WriteCore(new object(), in empty, new object(), ref state))
                        return 3;
                    if (converter.WriteCore(new object(), in present, new object(), ref state))
                        return 4;
                    if (converter.WriteCore(
                            new object(), in conditional, new object(), ref state))
                        return 8;
                    if (state.Marker != 13) return 5;
                    return 0;
                }
            }

            public static class Probe<T>
            {
                public static bool WriteCore(in T value) => TryWrite(in value);

                private static bool TryWrite(in T value) =>
                    default(T) is null && IsNull(value);

                private static bool IsNull(T value) => value is null;
            }

            public sealed class GenericNullProbe<T>
            {
                private bool _hasNull;

                public bool Add(T value)
                {
                    if (value is not null) return false;
                    _hasNull = true;
                    return _hasNull;
                }
            }

            public abstract class Converter<T>
            {
                public bool WriteCore(
                    object writer,
                    in T value,
                    object options,
                    ref WriteState state) =>
                    OnTryWrite(writer, value, options, ref state);

                protected abstract bool OnTryWrite(
                    object writer,
                    T value,
                    object options,
                    ref WriteState state);
            }

            public sealed class NullableConverter<T> : Converter<T?>
                where T : struct
            {
                protected override bool OnTryWrite(
                    object writer,
                    T? value,
                    object options,
                    ref WriteState state)
                {
                    state.Marker += 2;
                    return value is null;
                }
            }

            public sealed class TextConverter : Converter<string>
            {
                protected override bool OnTryWrite(
                    object writer,
                    string value,
                    object options,
                    ref WriteState state) => value.Length != 0;
            }

            public sealed class IntConverter : Converter<int>
            {
                protected override bool OnTryWrite(
                    object writer,
                    int value,
                    object options,
                    ref WriteState state) => value != int.MinValue;
            }

            public struct WriteState
            {
                public int Marker;
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            optimized ? "GenericNullableInOptimized" : "GenericNullableInDebug",
            source,
            "GenericNullableInFixture.EntryPoint",
            optimized,
            target,
            41,
            []));

        Assert.Equal(0, result);
    }

    [Fact]
    public void CompilerLowersNullableConstructionAccessAndCoalescing()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "NullableFixture",
            """
            namespace NullableFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int? present = Identity<int?>(input);
                    int? absent = null;
                    var score = present.HasValue ? present.Value : -100;
                    score += absent.GetValueOrDefault(1);
                    score += absent ?? 0;
                    return score;
                }

                private static T Identity<T>(T value) => value;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NullableFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerRootsEnumTypesRequiredByNullableTypeInspection(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;

            namespace NullableEnumTypeInspectionFixture;

            public enum State : byte
            {
                Ready = 41,
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    State? state = State.Ready;
                    var unrelated = Nullable.GetUnderlyingType(typeof(int?));
                    return unrelated == typeof(int) && state == State.Ready
                        ? input
                        : -1;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            optimized ? "NullableEnumTypeInspectionRelease" : "NullableEnumTypeInspectionDebug",
            source,
            "NullableEnumTypeInspectionFixture.EntryPoint",
            optimized,
            target,
            41,
            []));

        Assert.Equal(41, observed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompilerExecutesNullableSurfaceAcrossCilOptimizationAndTargetWidths(bool optimized)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;

            namespace NullableSurfaceFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int? present = input;
                    int? empty = default;
                    if (!present.HasValue || empty.HasValue) return 1;
                    if (present.Value != input || present.GetValueOrDefault() != input) return 2;
                    if (present.GetValueOrDefault(-1) != input ||
                        empty.GetValueOrDefault() != 0 ||
                        empty.GetValueOrDefault(-1) != -1) return 3;
                    if (present.ToString() != input.ToString() || empty.ToString() != "") return 4;
                    if (!present.Equals((object)input) || present.Equals((object)(input + 1)) ||
                        !empty.Equals(null) || empty.Equals((object)0)) return 5;
                    if (present.GetHashCode() != input.GetHashCode() || empty.GetHashCode() != 0) return 6;
                    if (Nullable.Compare(empty, empty) != 0 ||
                        Nullable.Compare(empty, present) >= 0 ||
                        Nullable.Compare(present, empty) <= 0 ||
                        Nullable.Compare(present, new int?(input)) != 0) return 7;
                    if (!Nullable.Equals(empty, empty) || Nullable.Equals(empty, present) ||
                        !Nullable.Equals(present, new int?(input)) ||
                        Nullable.Equals(present, new int?(input + 1))) return 8;
                    int? converted = input;
                    if ((int)converted != input) return 9;
                    try
                    {
                        _ = (int)empty;
                        return 10;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    ref readonly int presentReference = ref
                        Nullable.GetValueRefOrDefaultRef(ref present);
                    ref readonly int emptyReference = ref
                        Nullable.GetValueRefOrDefaultRef(ref empty);
                    if (presentReference != input || emptyReference != 0) return 11;
                    if (Nullable.GetUnderlyingType(typeof(int?)) != typeof(int)) return 12;
                    if (Nullable.GetUnderlyingType(typeof(int)) is not null) return 13;
                    try
                    {
                        _ = Nullable.GetUnderlyingType(null!);
                        return 14;
                    }
                    catch (ArgumentNullException)
                    {
                    }
                    object? boxedPresent = present;
                    object? boxedEmpty = empty;
                    if (boxedPresent is null || boxedEmpty is not null) return 15;
                    if ((int?)boxedPresent != present ||
                        ((int?)boxedEmpty).HasValue) return 16;
                    if (RoundTrip(present) != present || RoundTrip(empty).HasValue) return 17;
                    if (UnboxOnWrite<int?>(boxedPresent) != present ||
                        UnboxOnWrite<int?>(boxedEmpty).HasValue) return 18;
                    return 0;
                }

                private static T RoundTrip<T>(T value) => (T)(object?)value!;
                private static T? UnboxOnWrite<T>(object? value) => (T?)value;
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var result = executor.Execute(new CompilationScenario(
                optimized ? "NullableSurfaceOptimized" : "NullableSurfaceDebug",
                source,
                "NullableSurfaceFixture.EntryPoint",
                optimized,
                target,
                41,
                []));

            Assert.Equal(0, result);
        }
    }
}
