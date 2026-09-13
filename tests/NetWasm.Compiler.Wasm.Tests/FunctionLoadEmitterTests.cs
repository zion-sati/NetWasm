using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class FunctionLoadEmitterTests
{
    [Fact]
    public void OwnsLoadFunctionCommand()
    {
        var emitter = CreateEmitter(new FakeProgram(), WasmTarget.Wasm32);

        var command = Assert.Single(emitter.Commands);

        Assert.Equal(CilOperation.LoadFunction, command.Operation);
        Assert.Equal(
            InstructionFamily.CallsAndCallableLoading,
            command.Family);
    }

    [Fact]
    public void LoadsFunctionIndexAsNativeIntOnMemory32()
    {
        var program = new FakeProgram();
        var loader = AsLoader(CreateEmitter(program, WasmTarget.Wasm32));
        var request = CreateInstructionRequest(
            CilOperation.LoadFunction,
            operand: new CilOperand.Entity(EntryKey));
        var code = new RecordingInstructionWriter();

        loader.Load(request, code, CreateFunctionIndexResolver(program));

        Assert.Equal([CliValueKind.NativeInt], request.Stack);
        Assert.Contains(WasmOpcodes.I32Constant, code.ToArray());
        Assert.Contains(WasmOpcodes.LocalSet, code.ToArray());
    }

    [Fact]
    public void ExtendsFunctionIndexForNativeIntOnMemory64()
    {
        var program = new FakeProgram();
        var loader = AsLoader(CreateEmitter(program, WasmTarget.Wasm64));
        var request = CreateInstructionRequest(
            CilOperation.LoadFunction,
            operand: new CilOperand.Entity(EntryKey));
        var code = new RecordingInstructionWriter();

        loader.Load(request, code, CreateFunctionIndexResolver(program));

        Assert.Equal([CliValueKind.NativeInt], request.Stack);
        Assert.Contains(WasmOpcodes.I32Constant, code.ToArray());
        Assert.Contains(WasmOpcodes.I64ExtendI32Unsigned, code.ToArray());
        Assert.Contains(WasmOpcodes.LocalSet, code.ToArray());
    }

    private static FunctionLoadEmitter CreateEmitter(
        FakeProgram program,
        WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        return new FunctionLoadEmitter(
            layouts,
            CreateMethodOperands(program),
            new InstructionCommandFactory());
    }

    private static IFunctionLoader AsLoader(FunctionLoadEmitter emitter) =>
        new[] { emitter }.Cast<IFunctionLoader>().Single();
}
