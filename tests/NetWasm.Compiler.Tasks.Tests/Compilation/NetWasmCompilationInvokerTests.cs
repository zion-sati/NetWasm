using NetWasm.Compiler.Core;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Tasks.Compilation;
using NetWasm.Compiler.Tasks.Tests.TestSupport;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Tasks.Tests.Compilation;

public sealed class NetWasmCompilationInvokerTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, "wasm32")]
    [InlineData(WasmTarget.Wasm64, "wasm64")]
    public void CompilePreservesEveryMappedCompilerFact(WasmTarget target, string expectedTarget)
    {
        var symbols = new StackTraceSymbolArtifact([41, 42], "application/json", "symbols.json", "digest");
        var mapped = CompilerTaskTestData.CreateCompilation(expectedTarget, symbols,
            runtimeFeatures: [NetWasmRuntimeFeatureIds.LocalTime],
            functionImports: [new("host", "read", WasmFunctionType.Create(CliValueKind.I4))],
            nativeImports: [new("library", "compute", [WasmValueType.I32, WasmValueType.F64], WasmValueType.I64)]);
        // Program and Layouts are intentionally unobserved sentinels. This is
        // an adapter input, not a valid complete compiler-pipeline fixture.
        var result = new CompilationResult(mapped.CoreModule, null!, null!, mapped.InteropManifest, mapped.StaticDataEnd)
        {
            StackTraceSymbols = symbols,
            RuntimeFeatures = mapped.RuntimeFeatures,
            FunctionImports = mapped.FunctionImports,
            NativeImports = mapped.NativeImports,
        };
        var compiler = new RecordingCompiler(result);
        var invoker = Assert.IsAssignableFrom<INetWasmCompilationInvoker>(new NetWasmCompilationInvoker(compiler));
        var options = Options(target);

        var actual = invoker.Compile(options);

        Assert.Same(options, Assert.Single(compiler.Requests));
        Assert.Same(result.ApplicationModule, actual.CoreModule);
        Assert.Equal(result.StaticDataEnd, actual.StaticDataEnd);
        Assert.Same(result.InteropManifest, actual.InteropManifest);
        Assert.Same(symbols, actual.StackTraceSymbols);
        Assert.Equal(expectedTarget, actual.Target);
        Assert.Equal(result.RuntimeFeatures.ToArray(), actual.RuntimeFeatures.ToArray());
        Assert.Equal(result.FunctionImports.ToArray(), actual.FunctionImports.ToArray());
        Assert.Equal(result.NativeImports.ToArray(), actual.NativeImports.ToArray());
    }

    [Fact]
    public void CompileRejectsNullOptionsBeforeCallingTheCompiler()
    {
        var compiler = new RecordingCompiler(null!);
        var invoker = Assert.IsAssignableFrom<INetWasmCompilationInvoker>(new NetWasmCompilationInvoker(compiler));

        Assert.Throws<ArgumentNullException>(() => invoker.Compile(null!));

        Assert.Empty(compiler.Requests);
    }

    [Fact]
    public void CompilePropagatesTheOriginalCompilerFailure()
    {
        var failure = new InvalidOperationException("compiler failure");
        var compiler = new RecordingCompiler(null!) { Failure = failure };
        var invoker = Assert.IsAssignableFrom<INetWasmCompilationInvoker>(new NetWasmCompilationInvoker(compiler));
        var options = Options(WasmTarget.Wasm32);

        var actual = Assert.Throws<InvalidOperationException>(() => invoker.Compile(options));

        Assert.Same(failure, actual);
        Assert.Same(options, Assert.Single(compiler.Requests));
    }

    [Fact]
    public void ConstructorRejectsAnAbsentCompiler() =>
        Assert.Throws<ArgumentNullException>(() => new NetWasmCompilationInvoker(null!));

    private static CompilerOptions Options(WasmTarget target) =>
        new("memory/app.dll", [], "Entry", "Run", [], target);

    private sealed class RecordingCompiler(CompilationResult result) : INetWasmCompiler
    {
        public List<CompilerOptions> Requests { get; } = [];
        public Exception? Failure { get; init; }

        public CompilationResult Compile(CompilerOptions options)
        {
            Requests.Add(options);
            if (Failure is not null)
                throw Failure;
            return result;
        }
    }
}
