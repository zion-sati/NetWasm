namespace NetWasm.Compiler.Tasks.Compilation;

internal sealed class NetWasmCompilationInvoker(
    INetWasmCompiler compiler) : INetWasmCompilationInvoker
{
    private readonly INetWasmCompiler _compiler = compiler ??
        throw new ArgumentNullException(nameof(compiler));

    public ManagedModuleCompilation Compile(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var result = _compiler.Compile(options);
        return new(
            result.ApplicationModule,
            result.StaticDataEnd,
            result.InteropManifest,
            result.StackTraceSymbols,
            options.Target == Core.WasmTarget.Wasm64 ? "wasm64" : "wasm32",
            RuntimeFeatures: result.RuntimeFeatures,
            FunctionImports: result.FunctionImports);
    }
}
