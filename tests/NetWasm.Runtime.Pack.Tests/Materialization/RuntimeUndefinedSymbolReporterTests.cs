// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeUndefinedSymbolReporterTests
{
    private const string Log =
        "wasm-ld: error: /work/libSkiaSharp.a(GrGLInterface.o): undefined symbol: emscripten_set_main_loop\n" +
        "wasm-ld: error: /work/libSkiaSharp.a(GrGLGpu.o): undefined symbol: emscripten_get_now\n" +
        "wasm-ld: error: /work/libSkiaSharp.a(GrGLGpu.o): undefined symbol: emscripten_set_main_loop\n";

    private readonly IRuntimeUndefinedSymbolReporter _reporter = Assert.IsAssignableFrom<IRuntimeUndefinedSymbolReporter>(new RuntimeUndefinedSymbolReporter());

    [Fact]
    public void AComponentNamesEachFunctionOnceAndItsStandIns()
    {
        var failure = new InvalidOperationException("tool failed");

        var error = _reporter.Explain(Log, false, failure);

        Assert.NotNull(error);
        Assert.StartsWith(
            "Native code calls 'emscripten_get_now', 'emscripten_set_main_loop', which nothing in the program defines.",
            error.Message, StringComparison.Ordinal);
        Assert.Contains("A component has no JavaScript", error.Message, StringComparison.Ordinal);
        Assert.Same(failure, error.InnerException);
    }

    [Fact]
    public void APlainCoreModuleIsToldToDeclareThem()
    {
        var error = _reporter.Explain(Log, true, new InvalidOperationException());

        Assert.Contains("<NetWasmEnvImport Include=\"emscripten_get_now\"", error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherFailureIsLeftAsItIs()
    {
        Assert.Null(_reporter.Explain("wasm-ld: error: duplicate symbol: x\n", false, new InvalidOperationException()));
        Assert.Throws<ArgumentNullException>(() => _reporter.Explain(null!, false, new InvalidOperationException()));
        Assert.Throws<ArgumentNullException>(() => _reporter.Explain("", false, null!));
    }
}
