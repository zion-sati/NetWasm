using System;

namespace NetWasm.Compiler.ComponentModel.ManagedExecutables;

public sealed class NetWasmHostComponentShimWriter(
    IWasmTextModuleWriter modules) : INetWasmHostComponentShimWriter
{
    private readonly IWasmTextModuleWriter _modules = modules ??
        throw new ArgumentNullException(nameof(modules));

    public void Write(NetWasmHostComponentShimRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        ArgumentNullException.ThrowIfNull(request.Target);
        var addressType = request.Target.Width == "wasm64" ? "i64" : "i32";
        _modules.Write(
            "(module " +
            "(func (export \"write_i32\") (param i32)) " +
            "(func (export \"report_terminal_exception_v1\") " +
            $"(param i32 {addressType} i32) unreachable))",
            request.OutputPath);
    }
}
