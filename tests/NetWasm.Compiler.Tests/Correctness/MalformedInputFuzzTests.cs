// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class MalformedInputFuzzTests
{
    [Fact]
    public void RepeatedMalformedInputsProduceTheSameDiagnosticAndNoPartialWasm()
    {
        using var assets = TestAssets.Create();
        using var services = CorrectnessTestAssets.CreateServices();
        var generator = services.GetRequiredService<IMalformedInputGenerator>();
        var compiler = services.GetRequiredService<IMalformedCompilationRunner>();
        var baseline = MalformedInputGeneratorTests.CompileBaseline(assets);
        var mutations = generator.Generate(
            baseline,
            Path.Combine(assets.Directory, "mutations"));
        var requested = Environment.GetEnvironmentVariable(
            "NETWASM_MALFORMED_FUZZ_CASE");
        var selected = requested is null
            ? mutations
            : [.. mutations.Where(item => item.Name == requested)];
        if (requested is not null)
        {
            Assert.Single(selected);
        }
        foreach (var mutation in selected)
        {
            var diagnostics = new List<CompilerDiagnostic>();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var observation = compiler.Compile(
                    mutation,
                    assets.CoreLib,
                    "MalformedFuzzBaseline.EntryPoint",
                    assets.Directory,
                    attempt);
                Assert.Equal(QualifiedProcessCompletion.Exited, observation.Completion);
                Assert.NotNull(observation.Diagnostic);
                diagnostics.Add(observation.Diagnostic);
                Assert.False(observation.ModuleExists);
                Assert.True(observation.FailureSnapshotExists);
            }
            Assert.Equal(diagnostics[0].ToString(), diagnostics[1].ToString());
            Assert.Equal(diagnostics[0].Method, diagnostics[1].Method);
            Assert.Equal(diagnostics[0].IlOffset, diagnostics[1].IlOffset);
            if (mutation.Name != "invalid-metadata-signature")
            {
                Assert.True(
                    diagnostics[0].Method is not null,
                    $"{mutation.Name}: diagnostic has no method: {diagnostics[0]}");
                Assert.True(
                    diagnostics[0].IlOffset is not null,
                    $"{mutation.Name}: diagnostic has no IL offset: {diagnostics[0]}");
            }
        }
    }

    [Fact]
    public void RepeatedIllegalExceptionTransfersRejectWithoutPartialWasm()
    {
        using var assets = TestAssets.Create();
        using var services = CorrectnessTestAssets.CreateServices();
        var generator = services.GetRequiredService<IMalformedInputGenerator>();
        var compiler = services.GetRequiredService<IMalformedCompilationRunner>();
        var baseline = MalformedInputGeneratorTests.CompileExceptionBaseline(assets);
        var mutations = generator.GenerateExceptionHandling(
            baseline,
            Path.Combine(assets.Directory, "eh-mutations"));
        foreach (var mutation in mutations)
        {
            var diagnostics = new List<CompilerDiagnostic>();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var observation = compiler.Compile(
                    mutation,
                    assets.CoreLib,
                    "MalformedEhBaseline.EntryPoint",
                    assets.Directory,
                    attempt);
                Assert.Equal(QualifiedProcessCompletion.Exited, observation.Completion);
                Assert.NotNull(observation.Diagnostic);
                diagnostics.Add(observation.Diagnostic);
                Assert.False(observation.ModuleExists);
                Assert.True(observation.FailureSnapshotExists);
            }
            Assert.Equal(diagnostics[0].ToString(), diagnostics[1].ToString());
            Assert.NotNull(diagnostics[0].Method);
            Assert.NotNull(diagnostics[0].IlOffset);
        }
    }
}
