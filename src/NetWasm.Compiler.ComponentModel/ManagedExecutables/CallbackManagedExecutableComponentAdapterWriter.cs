using System;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.ManagedExecutables;

// An asynchronous Main is a callback-lifted wasi:cli/run. The application's run starts Main and
// returns the callback code, its netwasm.entry.callback takes each event, and once either returns
// EXIT the adapter returns Main's result through the export's task.return: a callback-lifted
// export returns through task.return, not its core result, and only the adapter knows which
// export's task is running. A structured command lifts netwasm:diagnostics/command's run the same
// way and returns its completion, a failure included.
public sealed class CallbackManagedExecutableComponentAdapterWriter(
    IWasmTextModuleWriter modules) : IManagedExecutableComponentAdapterWriter
{
    private const string RunInterface = "wasi:cli/run@0.3.2";
    private const string DiagnosticsInterface = "netwasm:diagnostics/command@1.0.0";

    private readonly IWasmTextModuleWriter _modules = modules ??
        throw new ArgumentNullException(nameof(modules));

    public void Write(ManagedExecutableComponentAdapterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        ArgumentNullException.ThrowIfNull(request.Target);
        ArgumentNullException.ThrowIfNull(request.EntryPoint);
        if (request.EntryPoint.CompletionShape != ManagedExecutableCompletionShape.Asynchronous)
        {
            throw new ArgumentException("The callback adapter requires asynchronous completion.", nameof(request));
        }
        var addressSize = request.Target.Width switch
        {
            "wasm32" => 4,
            "wasm64" => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(request),
                request.Target.Width, "Unsupported component target width."),
        };
        _ = request.EntryPoint.ParameterShape switch
        {
            ManagedExecutableParameterShape.None => true,
            ManagedExecutableParameterShape.StringArray => true,
            _ => throw new ArgumentOutOfRangeException(nameof(request),
                request.EntryPoint.ParameterShape, "Unsupported managed parameter shape."),
        };
        _ = request.EntryPoint.ReturnShape switch
        {
            ManagedExecutableReturnShape.Void => true,
            ManagedExecutableReturnShape.ExitCode => true,
            _ => throw new ArgumentOutOfRangeException(nameof(request),
                request.EntryPoint.ReturnShape, "Unsupported managed return shape."),
        };
        _modules.Write(request.StructuredDiagnostics
            ? CreateStructuredModule(addressSize)
            : CreateStandardModule(), request.OutputPath);
    }

    // The application's start, callback and exit code, which a Task Main reports as zero.
    private const string ApplicationImports =
        "(import \"netwasm.application.v1\" \"run\" (func $start (result i32))) " +
        "(import \"netwasm.application.v1\" \"netwasm.entry.callback\" " +
        "(func $callback (param i32 i32 i32) (result i32))) " +
        "(import \"netwasm.application.v1\" \"netwasm.entry.exit_code\" (func $exit_code (result i32))) " +
        $"(import \"[export]{RunInterface}\" \"[task-return]run\" (func $return_run (param i32))) ";

    private static string CreateStandardModule() =>
        "(module " + ApplicationImports +
        "(func $finish (param $code i32) (result i32) " +
        "local.get $code i32.eqz if call $exit_code i32.eqz i32.eqz call $return_run end " +
        "local.get $code) " +
        $"(func (export \"[async-lift]{RunInterface}#run\") (result i32) call $start call $finish) " +
        $"(func (export \"[callback][async-lift]{RunInterface}#run\") (param i32 i32 i32) (result i32) " +
        "local.get 0 local.get 1 local.get 2 call $callback call $finish))";

    // A terminal exception ends the task with the captured failure: wasi:cli/run writes it and
    // returns err, diagnostics' run returns it. The runtime's completion record holds the
    // canonical completion variant, which task.return takes flattened.
    private static string CreateStructuredModule(int addressSize)
    {
        var address = addressSize == 8 ? "i64" : "i32";
        string Load(string type, int offset) => $"local.get $completion {type}.load offset={offset} ";
        string Byte(int offset) => $"local.get $completion i32.load8_u offset={offset} ";
        return "(module " + ApplicationImports +
            $"(import \"netwasm.runtime.v1\" \"memory\" (memory {(addressSize == 8 ? "i64 " : string.Empty)}1)) " +
            "(import \"netwasm.host.v1\" \"terminal_exception\" (tag $terminal)) " +
            "(import \"netwasm.runtime.v1\" \"command_exception_write\" (func $write)) " +
            "(import \"netwasm.runtime.v1\" \"command_exception_completion\" " +
            $"(func $completion (param i32) (result {address}))) " +
            "(import \"netwasm.runtime.v1\" \"command_exception_release\" (func $release)) " +
            $"(import \"[export]{DiagnosticsInterface}\" \"[task-return]run\" " +
            $"(func $return_diagnostics (param i32 i32 i32 {address} {address} i32 {address} {address}))) " +
            "(global $entered (mut i32) (i32.const 0)) " +
            // -1 is a terminal failure; anything else is the callback code.
            "(func $guarded_start (result i32) " +
            "global.get $entered if unreachable end i32.const 1 global.set $entered " +
            "(block $failed (try_table (catch $terminal $failed) call $start return)) i32.const -1) " +
            "(func $guarded_callback (param i32 i32 i32) (result i32) " +
            "(block $failed (try_table (catch $terminal $failed) " +
            "local.get 0 local.get 1 local.get 2 call $callback return)) i32.const -1) " +
            "(func $finish_run (param $code i32) (result i32) " +
            "local.get $code i32.const -1 i32.eq if call $write i32.const 1 call $return_run i32.const 0 return end " +
            "local.get $code i32.eqz if call $exit_code i32.eqz i32.eqz call $return_run end " +
            "local.get $code) " +
            "(func $finish_diagnostics (param $code i32) (result i32) " +
            $"(local $completion {address}) " +
            "local.get $code i32.const -1 i32.eq local.get $code i32.eqz i32.or if " +
            "call $exit_code i32.eqz i32.eqz call $completion local.set $completion " +
            Byte(0) + Load("i32", addressSize) + Byte(2 * addressSize) +
            Load(address, 3 * addressSize) + Load(address, 4 * addressSize) + Byte(5 * addressSize) +
            Load(address, 6 * addressSize) + Load(address, 7 * addressSize) +
            "call $return_diagnostics call $release i32.const 0 return end " +
            "local.get $code) " +
            $"(func (export \"[async-lift]{RunInterface}#run\") (result i32) " +
            "call $guarded_start call $finish_run) " +
            $"(func (export \"[callback][async-lift]{RunInterface}#run\") (param i32 i32 i32) (result i32) " +
            "local.get 0 local.get 1 local.get 2 call $guarded_callback call $finish_run) " +
            $"(func (export \"[async-lift]{DiagnosticsInterface}#run\") (result i32) " +
            "call $guarded_start call $finish_diagnostics) " +
            $"(func (export \"[callback][async-lift]{DiagnosticsInterface}#run\") (param i32 i32 i32) (result i32) " +
            "local.get 0 local.get 1 local.get 2 call $guarded_callback call $finish_diagnostics))";
    }
}
