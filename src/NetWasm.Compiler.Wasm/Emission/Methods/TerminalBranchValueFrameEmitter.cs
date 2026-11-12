using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class TerminalBranchValueFrameEmitter(
    ITargetLayout layouts,
    IStructuredConditionValueEmitter conditions,
    IValueFrameEntryEmitter valueFrames,
    IRuntimeImportResolver runtimeImports) : ITerminalBranchValueFrameEmitter
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
        var defersRoots = placement.Plan.DefersRootFrame;
        if (context.ValueLayout.Size != 0 || (placement.FramedLayout.Size == 0 && !defersRoots) ||
            context.FilterEnvironment.Size != 0 || context.ExceptionRootSlots.Count != 0 ||
            (defersRoots && (context.RootSlotCount != 0 || placement.FramedRootMap is null)) ||
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
            if (defersRoots)
            {
                framedContext = framedContext with
                {
                    RootMap = placement.FramedRootMap!,
                    AuthoritativeRootSlots = placement.FramedAuthoritativeRootSlots,
                };
                EnterRootFrame(framedContext);
            }
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

        // The root frame the prologue would have entered, entered where the arm begins: each
        // authoritative slot takes its variable's value there, which the code before the arm may
        // already have set.
        void EnterRootFrame(MethodEmissionContext framed)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(framed.RootSlotCount)));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.RootFrameEnter))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)framed.RootFrame)));
            foreach (var (source, slot) in framed.AuthoritativeRootSlots.OrderBy(pair => pair.Value))
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)framed.RootFrame)));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)(source.Index + (source.Kind == RootSourceKind.Local
                        ? framed.LocalBase : framed.ParameterOffset)))));
                ManagedMemoryEmitter.EmitStoreBySize(code, layouts.Target,
                    checked(slot * layouts.Target.ObjectReferenceSize), layouts.Target.ObjectReferenceSize);
            }
        }
    }
}
