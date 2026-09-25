using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class StaticInitializerFunctionEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver imports,
    IAddressInstructionEmitter addresses,
    IExceptionPayloadBlockEmitter payloads,
    IGeneratedFunctionWriterFactory writers) : IStaticInitializerFunctionEmitter
{
    public byte[] Emit(StaticInitializerFunction initializer, StaticInitializerFunctionPlan plan)
    {
        var output = writers.Create();
        var code = output.Instructions;
        const int state = 0;
        const int frame = 1;
        const int exception = 2;
        WasmLocalDeclarationWriter.Write(output.Bytes,
            [CliValueKind.I4, CliValueKind.I4, CliValueKind.ManagedReference], layouts.Target);
        addresses.Emit(code, initializer.GuardAddress);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, 0)));
        Local(WasmOpcodes.LocalTee, state);
        Constant(1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        Constant(2);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
        Block(WasmOpcodes.If);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        End();

        Local(WasmOpcodes.LocalGet, state);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        Block(WasmOpcodes.If);
        addresses.Emit(code, initializer.GuardAddress);
        Constant(1);
        Store();
        addresses.Emit(code, plan.CatchMetadataAddress);
        Constant(1);
        Call(RuntimeImportSymbol.ExceptionFrameEnter);
        Local(WasmOpcodes.LocalSet, frame);
        payloads.Emit(code);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatch(WasmOpcodes.EmptyBlockType, 0, 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)initializer.InitializerIndex)));
        End();
        Local(WasmOpcodes.LocalGet, frame);
        Call(RuntimeImportSymbol.ExceptionFrameLeave);
        addresses.Emit(code, initializer.GuardAddress);
        Constant(2);
        Store();
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        End();
        Local(WasmOpcodes.LocalSet, exception);
        Local(WasmOpcodes.LocalGet, frame);
        Call(RuntimeImportSymbol.ExceptionFrameLeave);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        Local(WasmOpcodes.LocalGet, state);
        Constant(2);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        Call(RuntimeImportSymbol.HandleGet);
        Local(WasmOpcodes.LocalSet, exception);
        End();
        addresses.Emit(code, initializer.GuardAddress);
        Local(WasmOpcodes.LocalGet, exception);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)plan.FailureFunctionIndex)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        End();
        return output.Snapshots.Read();

        void Constant(int value) => code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
        void Local(byte opcode, int index) => code.Write(WasmInstruction.WithOperand(opcode, WasmInstructionOperand.Unsigned((uint)index)));
        void Block(byte opcode) => code.Write(WasmInstruction.WithOperand(opcode, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        void Call(RuntimeImportSymbol symbol) => code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)imports.Resolve(symbol))));
        void Store() => code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Store, WasmInstructionOperand.Memory(2, 0)));
        void End() => code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
