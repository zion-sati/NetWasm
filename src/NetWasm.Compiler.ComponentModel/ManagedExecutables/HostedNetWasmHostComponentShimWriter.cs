using System;

namespace NetWasm.Compiler.ComponentModel.ManagedExecutables;

public sealed class HostedNetWasmHostComponentShimWriter(
    IWasmTextModuleWriter modules) : INetWasmHostComponentShimWriter
{
    private readonly IWasmTextModuleWriter _modules = modules ??
        throw new ArgumentNullException(nameof(modules));

    public void Write(NetWasmHostComponentShimRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        ArgumentNullException.ThrowIfNull(request.Target);
        var memory64 = request.Target.Width == "wasm64";
        var addressType = memory64 ? "i64" : "i32";
        var prefix = memory64 ? "cm64p2" : "cm32p2";
        var stringDataOffset = memory64 ? 12 : 8;
        var lowerLength = memory64 ? "i64.extend_i32_u " : string.Empty;
        _modules.Write(
            "(module " +
            $"(import \"{prefix}|netwasm:diagnostics/terminal@1\" \"report\" " +
            $"(func $report (param i32 i32 {addressType} {addressType} i32 {addressType} {addressType}))) " +
            "(func (export \"write_i32\") (param i32)) " +
            "(func (export \"raise_terminal_exception\") unreachable) " +
            "(func (export \"report_terminal_exception_v2\") " +
            $"(param $type i32) (param $message {addressType}) (param $message_length i32) " +
            $"(param $stack_trace {addressType}) (param $stack_trace_length i32) " +
            "local.get $type " +
            "local.get $message " +
            $"{addressType}.eqz if (result i32) i32.const 0 else i32.const 1 end " +
            "local.get $message " +
            $"{addressType}.eqz if (result {addressType}) {addressType}.const 0 " +
            $"else local.get $message {addressType}.const {stringDataOffset} {addressType}.add end " +
            $"local.get $message_length {lowerLength}" +
            "local.get $stack_trace " +
            $"{addressType}.eqz if (result i32) i32.const 0 else i32.const 1 end " +
            "local.get $stack_trace " +
            $"{addressType}.eqz if (result {addressType}) {addressType}.const 0 " +
            $"else local.get $stack_trace {addressType}.const {stringDataOffset} {addressType}.add end " +
            $"local.get $stack_trace_length {lowerLength}call $report))",
            request.OutputPath);
    }
}
