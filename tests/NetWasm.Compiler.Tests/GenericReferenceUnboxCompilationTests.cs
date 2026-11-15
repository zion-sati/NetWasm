// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class GenericReferenceUnboxCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesReferenceSpecializedGenericUnboxAny(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "GenericReferenceUnboxFixture",
            Source,
            "GenericReferenceUnboxFixture.EntryPoint",
            optimize,
            target,
            0,
            []));

        Assert.Equal(0, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesNestedReferenceSpecializedGenericUnboxAny(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "NestedGenericReferenceUnboxFixture",
            NestedSource,
            "NestedGenericReferenceUnboxFixture.EntryPoint",
            optimize,
            target,
            0,
            []));

        Assert.Equal(0, observed);
    }

    private const string Source =
        """
        namespace GenericReferenceUnboxFixture;

        public interface IMarker;

        public sealed class Marker : IMarker;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                object value = new Marker();
                if (value is not IMarker)
                {
                    return 1;
                }

                if (!Is<IMarker>(value))
                {
                    return 2;
                }

                var cast = Cast<IMarker>(value);
                return object.ReferenceEquals(value, cast) ? 0 : 3;
            }

            private static bool Is<T>(object value) => value is T;

            private static T Cast<T>(object value) => (T)value;
        }
        """;

    private const string NestedSource =
        """
        namespace NestedGenericReferenceUnboxFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                object value = new Marker();
                if (value is not IMarker)
                {
                    return 1;
                }

                if (!Is<IMarker>(value))
                {
                    return 2;
                }

                var cast = Cast<IMarker>(value);
                return object.ReferenceEquals(value, cast) ? 0 : 3;
            }

            private static bool Is<T>(object value) => value is T;

            private static T? Cast<T>(object value) => (T?)value;

            private interface IMarker;

            private sealed class Marker : IMarker;
        }
        """;
}
