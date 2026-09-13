using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using Xunit;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class StackTraceFrameEntryEmitterTests
{
    [Fact]
    public void EntersTheResolvedDiagnosticFrame()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = Create(imports);
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Enter(code, 37, StackTraceImports);

        Assert.Collection(code.ToInstructions(),
            instruction =>
            {
                Assert.Equal(WasmOpcodes.I32Constant, instruction.Opcode);
                Assert.Equal(37, instruction.Operand.SignedValue);
            },
            instruction =>
            {
                Assert.Equal(WasmOpcodes.Call, instruction.Opcode);
                Assert.Equal(
                    (uint)imports.Resolve(
                        RuntimeImportSymbol.StackTraceFrameEnter,
                        StackTraceImports),
                    instruction.Operand.UnsignedValue);
            });
    }

    [Fact]
    public void RejectsMissingWriterAndInvalidMethodId()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = Create(imports);
        Assert.Throws<ArgumentNullException>(() =>
            emitter.Enter(null!, 1, StackTraceImports));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            emitter.Enter(
                new EmitterTestSupport.RecordingInstructionWriter(),
                0,
                StackTraceImports));
    }

    private static RuntimeImportSelection StackTraceImports => new(
        WasmModuleProfile.CoreApplication,
        IncludeTerminalExceptionReporter: true,
        IncludeStackTrace: true);

    private static readonly Func<IRuntimeImportResolver, IStackTraceFrameEntryEmitter>
        Create = static imports => new StackTraceFrameEntryEmitter(imports);
}
