// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.TestInfrastructure;

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerWasmTextWriterTests
{
    [Fact]
    public void PrintsAValidModuleWithTheAuthoritativeWasmTools()
    {
        using var assets = TestAssets.Create();
        var result = NetWasmCompiler.Compile(CompilerTestSupport.Options(assets, []));
        var wasm = Path.Combine(assets.Directory, "module.wasm");
        var wat = Path.Combine(assets.Directory, "module.wat");
        File.WriteAllBytes(wasm, result.ApplicationModule);

        var writer = new CompilerWasmTextWriter(
            new RecordingToolRunner("(module)"),
            new CompilerDiagnosticTextArtifactWriter());
        writer.WriteText(wasm, wat);

        Assert.Contains("(module", File.ReadAllText(wat));
        Assert.Throws<ArgumentException>(() => writer.WriteText("", wat));
        Assert.Throws<ArgumentException>(() => writer.WriteText(wasm, ""));
    }

    [Fact]
    public void ReportsToolFailureWithoutStartingARealProcess()
    {
        using var assets = TestAssets.Create();
        var writer = new CompilerWasmTextWriter(
            new RecordingToolRunner("", 7, "bad module"),
            new CompilerDiagnosticTextArtifactWriter());

        var exception = Assert.Throws<InvalidOperationException>(() => writer.WriteText(
            Path.Combine(assets.Directory, "module.wasm"),
            Path.Combine(assets.Directory, "module.wat")));

        Assert.Contains("exit code 7", exception.Message, StringComparison.Ordinal);
        Assert.Contains("bad module", exception.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingToolRunner(
        string output,
        int exitCode = 0,
        string error = "") : IExternalToolRunner
    {
        public ToolResult Run(string executable, IEnumerable<string> arguments) =>
            new(exitCode, output, error);
    }
}
