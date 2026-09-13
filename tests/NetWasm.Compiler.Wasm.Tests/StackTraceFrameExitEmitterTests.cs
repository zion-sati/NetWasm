using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using Xunit;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class StackTraceFrameExitEmitterTests
{
    [Fact]
    public void LeavesTheResolvedDiagnosticFrame()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = Create(imports);
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Leave(code, 41, StackTraceImports);

        Assert.Collection(code.ToInstructions(),
            instruction =>
            {
                Assert.Equal(WasmOpcodes.I32Constant, instruction.Opcode);
                Assert.Equal(41, instruction.Operand.SignedValue);
            },
            instruction =>
            {
                Assert.Equal(WasmOpcodes.Call, instruction.Opcode);
                Assert.Equal(
                    (uint)imports.Resolve(
                        RuntimeImportSymbol.StackTraceFrameLeave,
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
            emitter.Leave(null!, 1, StackTraceImports));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            emitter.Leave(
                new EmitterTestSupport.RecordingInstructionWriter(),
                -1,
                StackTraceImports));
    }

    private static RuntimeImportSelection StackTraceImports => new(
        WasmModuleProfile.CoreApplication,
        IncludeTerminalExceptionReporter: true,
        IncludeStackTrace: true);

    private static readonly Func<IRuntimeImportResolver, IStackTraceFrameExitEmitter>
        Create = static imports => new StackTraceFrameExitEmitter(imports);
}
