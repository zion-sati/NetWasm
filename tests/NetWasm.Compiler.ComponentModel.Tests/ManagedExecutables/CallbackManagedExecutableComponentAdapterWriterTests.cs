using NetWasm.Compiler.ComponentModel.ManagedExecutables;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Tests.ManagedExecutables;

public sealed class CallbackManagedExecutableComponentAdapterWriterTests
{
    public static TheoryData<string, ManagedExecutableParameterShape, ManagedExecutableReturnShape> Shapes
    {
        get
        {
            var shapes = new TheoryData<string, ManagedExecutableParameterShape, ManagedExecutableReturnShape>();
            foreach (var width in new[] { "wasm32", "wasm64" })
                foreach (var parameter in Enum.GetValues<ManagedExecutableParameterShape>())
                    foreach (var result in Enum.GetValues<ManagedExecutableReturnShape>())
                        shapes.Add(width, parameter, result);
            return shapes;
        }
    }

    // run is lifted with a callback; EXIT returns Main's exit code through run's task.return.
    [Theory]
    [MemberData(nameof(Shapes))]
    public void LiftsRunWithACallbackAndReturnsThroughTaskReturn(
        string width, ManagedExecutableParameterShape parameter, ManagedExecutableReturnShape result)
    {
        var modules = new RecordingWasmTextModuleWriter();
        var writer = Assert.IsAssignableFrom<IManagedExecutableComponentAdapterWriter>(
            new CallbackManagedExecutableComponentAdapterWriter(modules));

        writer.Write(new("output.wasm", new(width, "0.3", "utf8"),
            new(parameter, result, ManagedExecutableCompletionShape.Asynchronous)));

        Assert.Equal("output.wasm", modules.OutputPath);
        Assert.Contains("(func (export \"[async-lift]wasi:cli/run@0.3.2#run\") (result i32) call $start call $finish)", modules.Source);
        Assert.Contains("(func (export \"[callback][async-lift]wasi:cli/run@0.3.2#run\") (param i32 i32 i32) (result i32) " +
            "local.get 0 local.get 1 local.get 2 call $callback call $finish)", modules.Source);
        Assert.Contains("(import \"[export]wasi:cli/run@0.3.2\" \"[task-return]run\" (func $return_run (param i32)))", modules.Source);
        Assert.Contains("local.get $code i32.eqz if call $exit_code i32.eqz i32.eqz call $return_run end", modules.Source);
        Assert.Contains("\"netwasm.entry.callback\"", modules.Source);
        Assert.Contains("\"netwasm.entry.exit_code\"", modules.Source);
        Assert.DoesNotContain("netwasm:diagnostics", modules.Source);
        Assert.DoesNotContain("memory", modules.Source);
    }

    // A structured command also lifts diagnostics' run, whose completion task.return takes
    // flattened from the runtime's record; a terminal exception is the failed completion.
    [Theory]
    [InlineData("wasm32", "i32", 4, "")]
    [InlineData("wasm64", "i64", 8, "i64 ")]
    public void AStructuredCommandReturnsItsCompletionThroughBothExports(
        string width, string address, int size, string memoryIndex)
    {
        var modules = new RecordingWasmTextModuleWriter();
        new CallbackManagedExecutableComponentAdapterWriter(modules).Write(new("output.wasm",
            new(width, "0.3", "utf8"),
            new(ManagedExecutableParameterShape.None, ManagedExecutableReturnShape.ExitCode,
                ManagedExecutableCompletionShape.Asynchronous),
            StructuredDiagnostics: true));

        foreach (var export in new[] { "wasi:cli/run@0.3.2", "netwasm:diagnostics/command@1.0.0" })
        {
            Assert.Contains($"(func (export \"[async-lift]{export}#run\")", modules.Source);
            Assert.Contains($"(func (export \"[callback][async-lift]{export}#run\")", modules.Source);
        }
        Assert.Contains($"(import \"netwasm.runtime.v1\" \"memory\" (memory {memoryIndex}1))", modules.Source);
        Assert.Contains($"(func $return_diagnostics (param i32 i32 i32 {address} {address} i32 {address} {address}))", modules.Source);
        Assert.Contains($"(func $completion (param i32) (result {address}))", modules.Source);
        Assert.Contains("(try_table (catch $terminal $failed) call $start return)) i32.const -1)", modules.Source);
        Assert.Contains("if call $write i32.const 1 call $return_run i32.const 0 return end", modules.Source);
        Assert.Contains($"local.get $completion {address}.load offset={7 * size} " +
            "call $return_diagnostics call $release i32.const 0 return end", modules.Source);
        Assert.Contains($"local.get $completion i32.load8_u offset={5 * size} ", modules.Source);
    }

    [Fact]
    public void RejectsInvalidInputsWithoutWritingAModule()
    {
        var modules = new RecordingWasmTextModuleWriter();
        var writer = new CallbackManagedExecutableComponentAdapterWriter(modules);
        var request = new ManagedExecutableComponentAdapterRequest("output.wasm", ComponentTarget.Wasm32Wasi02,
            new(ManagedExecutableParameterShape.None, ManagedExecutableReturnShape.Void,
                ManagedExecutableCompletionShape.Asynchronous));
        Assert.Throws<ArgumentNullException>(() => new CallbackManagedExecutableComponentAdapterWriter(null!));
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
        Assert.Throws<ArgumentException>(() => writer.Write(request with { OutputPath = " " }));
        Assert.Throws<ArgumentNullException>(() => writer.Write(request with { Target = null! }));
        Assert.Throws<ArgumentNullException>(() => writer.Write(request with { EntryPoint = null! }));
        Assert.Throws<ArgumentException>(() => writer.Write(request with
        { EntryPoint = request.EntryPoint with { CompletionShape = ManagedExecutableCompletionShape.Synchronous } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Write(request with { Target = new("invalid", "0.3", "utf8") }));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Write(request with
        { EntryPoint = request.EntryPoint with { ParameterShape = (ManagedExecutableParameterShape)99 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Write(request with
        { EntryPoint = request.EntryPoint with { ReturnShape = (ManagedExecutableReturnShape)99 } }));
        Assert.Empty(modules.OutputPath);
        Assert.Empty(modules.Source);
    }
}
