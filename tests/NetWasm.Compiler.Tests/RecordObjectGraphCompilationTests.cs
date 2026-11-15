// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class RecordObjectGraphCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerConstructsFrameworkStyleRecordObjectGraphs(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "RecordObjectGraphFixture",
            Source,
            "RecordObjectGraphFixture.EntryPoint",
            optimize,
            target,
            -2,
            ImmutableArray<string>.Empty));

        Assert.Equal(998, result);
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;

        namespace RecordObjectGraphFixture;

        public enum Kind { Unknown, Alpha, Beta }
        public readonly record struct Marker(int Value);
        public static class Nested
        {
            public sealed record Pair<T>(T First, T Second);
        }
        public sealed record Envelope(
            string Name,
            int? Count,
            Kind Kind,
            List<int> Values,
            Dictionary<string, int?> Metrics,
            Nested.Pair<Marker> Pair);

        public static class EntryPoint
        {
            private static int stage;

            public static int Run(int input)
            {
                try
                {
                    stage = -1;
                    var name = "fixture-" + input;
                    stage = -2;
                    int? count = input % 2 == 0 ? input : null;
                    stage = -3;
                    var kind = (input % 3) switch
                    {
                        0 => Kind.Alpha,
                        1 => Kind.Beta,
                        _ => Kind.Unknown,
                    };
                    stage = -4;
                    var values = new List<int> { input, input + 1 };
                    stage = -5;
                    var metrics = new Dictionary<string, int?>
                    {
                        ["input"] = input,
                        ["nullable"] = input % 2 == 0 ? null : input + 10,
                    };
                    stage = -6;
                    var pair = new Nested.Pair<Marker>(new(input + 4), new(input + 8));
                    stage = -7;
                    var envelope = new Envelope(name, count, kind, values, metrics, pair);
                    stage = -8;
                    return envelope.Name == name &&
                           envelope.Count == count &&
                           envelope.Kind == kind &&
                           envelope.Values.Count == 2 &&
                           envelope.Metrics["input"] == input &&
                           envelope.Pair.First.Value == input + 4
                        ? 1000 + input
                        : -9;
                }
                catch (InvalidCastException)
                {
                    return stage;
                }
            }
        }
        """;
}
