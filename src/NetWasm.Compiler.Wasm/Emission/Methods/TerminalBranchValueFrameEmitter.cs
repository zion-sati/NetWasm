using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class TerminalBranchValueFrameEmitter(
    ITargetLayout layouts,
    IStructuredConditionValueEmitter conditions,
    IValueFrameEntryEmitter valueFrames) : ITerminalBranchValueFrameEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        StructuredMethodHeader header,
        MethodEmissionContext context,
        TerminalBranchValueFramePlacement placement,
        Func<StructuredBlockOccurrence, MethodEmissionContext, ConditionValue> emitCondition,
        Action<StructuredSequence, MethodEmissionContext> emitBody)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(emitCondition);
        ArgumentNullException.ThrowIfNull(emitBody);
        if (context.ValueLayout.Size != 0 || placement.FramedLayout.Size == 0 ||
            context.FilterEnvironment.Size != 0 || context.ExceptionRootSlots.Count != 0 ||
            placement.Plan.IncomingScalarLocals.Any(index =>
                !context.AddressTakenScalarPromotions.ShadowLocals.ContainsKey(index)))
        {
            throw new InvalidOperationException("Branch value-frame emission requires separate register and frame bindings.");
        }

        conditions.Emit(code, context, emitCondition(placement.Plan.Condition, context));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitArm(placement.Plan.WhenTrue, placement.Plan.FramedWhenTrue);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        EmitArm(placement.Plan.WhenFalse, !placement.Plan.FramedWhenTrue);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        void EmitArm(StructuredSequence sequence, bool framed)
        {
            if (!framed)
            {
                emitBody(sequence, context);
                return;
            }
            var framedContext = context with
            {
                ValueLayout = placement.FramedLayout,
                ValueFrameReferences = placement.FramedReferences,
            };
            valueFrames.Emit(code, header, framedContext);
            foreach (var index in placement.Plan.IncomingScalarLocals.Order())
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)context.AddressTakenScalarPromotions.ShadowLocals[index])));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)(context.LocalBase + index))));
                var type = header.LocalSignatureTypes[index];
                ManagedMemoryEmitter.EmitStoreByType(code, layouts.Target, 0, type,
                    layouts.Target.GetStorageSize(type));
            }
            emitBody(sequence, framedContext);
        }
    }
}
