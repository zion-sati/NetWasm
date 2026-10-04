using System;

namespace NetWasm.Compiler.ComponentModel.ManagedExecutables;

public sealed class PortableCommandHostComponentShimWriter(
    IWasmTextModuleWriter modules) : INetWasmHostComponentShimWriter
{
    private readonly IWasmTextModuleWriter _modules = modules ??
        throw new ArgumentNullException(nameof(modules));

    public void Write(NetWasmHostComponentShimRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        ArgumentNullException.ThrowIfNull(request.Target);
        var addressType = request.Target.Width switch
        {
            "wasm32" => "i32",
            "wasm64" => "i64",
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
        _modules.Write(request.StructuredDiagnostics
            ? CreateStructuredModule(addressType)
            : CreateStandardModule(addressType),
            request.OutputPath);
    }

    private static string CreateStandardModule(string addressType) =>
        "(module " +
        "(func (export \"write_i32\") (param i32)) " +
        "(func (export \"report_terminal_exception_v2\") " +
        $"(param i32 {addressType} i32 {addressType} i32)) " +
        "(func (export \"raise_terminal_exception\") unreachable))";

    private static string CreateStructuredModule(string addressType) =>
            "(module " +
            "(import \"netwasm.runtime.v1\" \"command_exception_capture\" " +
            $"(func $capture (param i32 {addressType} i32 {addressType} i32))) " +
            "(tag $terminal (export \"terminal_exception\")) " +
            "(func (export \"write_i32\") (param i32)) " +
            "(func (export \"report_terminal_exception_v2\") " +
            $"(param i32 {addressType} i32 {addressType} i32) " +
            "local.get 0 local.get 1 local.get 2 local.get 3 local.get 4 call $capture) " +
            "(func (export \"raise_terminal_exception\") throw $terminal))";
}
