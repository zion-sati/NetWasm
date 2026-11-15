// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Tests;

public sealed class DiagnosticsActorContractTests
{
    [Fact]
    public void ComplexityCollectorConsumesOnlyTheSymbolFormattingCapability()
    {
        var program = ValidationTestData.Program();
        var method = program.Methods.Values.Single();
        var identity = method.Method.Definition.Key.ToString();
        var lowering = ValidationTestData.Lower(program);
        var symbols = new RecordingSymbols();
        var metric = new WasmManagedMethodEmissionMetric(
            identity,
            1,
            1,
            1,
            1,
            ImmutableDictionary<int, int>.Empty.Add(0, 1));

        var report = new CompilerComplexityMetricsCollector(
            NullLogger<CompilerComplexityMetricsCollector>.Instance,
            new StructuredBlockOwnershipAnalyzer()).Collect(
            symbols,
            program,
            lowering,
            [metric]);

        Assert.Equal("formatted method", Assert.Single(report.Methods).Identity);
        Assert.Equal(1, symbols.MethodFormats);
    }

    private sealed class RecordingSymbols : ISymbolFormatter
    {
        public int MethodFormats { get; private set; }

        public string Format(EntityKey key) => key.ToString();

        public string Format(MethodDefinitionModel method)
        {
            MethodFormats++;
            return "formatted method";
        }
    }
}
