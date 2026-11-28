// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusRuntimeAsyncOverrideTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void RuntimeAsyncIsOnUnlessTheOverrideIsZero(string? value, bool enabled) =>
        Assert.Equal(enabled, CorpusRuntimeAsyncOverride.Parse(value).Enabled);

    [Theory]
    [InlineData("true")]
    [InlineData("on")]
    public void AnyOtherRuntimeAsyncOverrideIsRejected(string value) =>
        Assert.Contains("NETWASM_CORPUS_RUNTIME_ASYNC", Assert.Throws<InvalidOperationException>(() =>
            CorpusRuntimeAsyncOverride.Parse(value)).Message, StringComparison.Ordinal);

    // With the override on, the NetWasm side's async method is Roslyn's runtime-async output (MethodImplOptions.Async,
    // no state machine type), while the desktop side, the oracle, keeps its state machine.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheOverrideCompilesTheNetWasmSideWithRuntimeAsync(bool enabled)
    {
        var compilation = new RoslynCorpusCompiler(
            CompilerCorrectnessEnvironment.Discover(),
            new QualifiedProcessRunner(),
            CorrectnessTestAssets.CreateOracleModes(), new CorpusSourceNamesVerifier(),
            new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier()),
            new CorpusRuntimeAsyncOverride(enabled)).Compile(
            RuntimeAsyncFixture(),
            CilProfile.Release,
            CorrectnessTestAssets.CreateDirectory());

        Assert.Equal(enabled, compilation.NetWasm.CompilerOptions.Contains("-features:runtime-async=on"));
        Assert.DoesNotContain("-features:runtime-async=on", compilation.Desktop.CompilerOptions);
        Assert.Equal(enabled, IsRuntimeAsync(compilation.NetWasm.AssemblyPath));
        Assert.False(IsRuntimeAsync(compilation.Desktop.AssemblyPath));
    }

    // A mode that runs one assembly on both sides compiles it with runtime async when the override is on, so NetWasm's
    // side still lowers it, against desktop .NET's runtime async on the same IL.
    [Fact]
    public void ASharedAssemblyTakesRuntimeAsyncOnBothSides()
    {
        var compilation = new RoslynCorpusCompiler(
            CompilerCorrectnessEnvironment.Discover(),
            new QualifiedProcessRunner(),
            CorrectnessTestAssets.CreateOracleModes(), new CorpusSourceNamesVerifier(),
            new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier()),
            new CorpusRuntimeAsyncOverride(true)).Compile(
            RuntimeAsyncFixture() with { OracleMode = OracleMode.SameIl, SameSourceReason = null },
            CilProfile.Release,
            CorrectnessTestAssets.CreateDirectory());

        Assert.Equal(compilation.Desktop.AssemblyPath, compilation.NetWasm.AssemblyPath);
        Assert.Contains("-features:runtime-async=on", compilation.Desktop.CompilerOptions);
        Assert.True(IsRuntimeAsync(compilation.NetWasm.AssemblyPath));
    }

    private static CorpusFixture RuntimeAsyncFixture() => new(
        "RuntimeAsyncCorpusUnit",
        "NetWasm.Correctness.RuntimeAsyncCorpusUnit",
        """
        namespace NetWasm.Correctness.RuntimeAsyncCorpusUnit;

        public static class EntryPoint
        {
            public static int Run(int input) => AddAsync(input).GetAwaiter().GetResult();

            private static async System.Threading.Tasks.Task<int> AddAsync(int value)
            {
                await System.Threading.Tasks.Task.Yield();
                return value + 1;
            }
        }
        """,
        [3]);

    // Whether AddAsync is marked MethodImplOptions.Async and the assembly has no state machine type for it.
    private static bool IsRuntimeAsync(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        var method = metadata.MethodDefinitions.Select(metadata.GetMethodDefinition)
            .Single(definition => metadata.GetString(definition.Name) == "AddAsync");
        var stateMachine = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Any(type => metadata.GetString(type.Name).StartsWith("<AddAsync>", StringComparison.Ordinal));
        return ((int)method.ImplAttributes & 0x2000) != 0 && !stateMachine;
    }
}
