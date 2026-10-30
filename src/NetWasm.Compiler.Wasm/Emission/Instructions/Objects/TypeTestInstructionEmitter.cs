using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class TypeTestInstructionEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    ITypeLayoutProvider typeLayouts,
    ICilTypeOperandResolver types,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    INullableTypeResolver nullableTypes) :
    InstructionCommandProvider,
    ITypeTestEmitter
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.CastClass, (request, code) => Test(request, code, false)),
        Command(CilOperation.IsInstance, (request, code) => Test(request, code, true)),
    ];

    private static InstructionCommand Command(
        CilOperation operation,
        Action<InstructionEmissionRequest, IWasmInstructionWriter> emit) => new(
        operation,
        InstructionFamily.AllocationBoxingTypes,
        emit);

    public void Test(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        bool returnNullOnFailure)
    {
        var value = request.Stack.Count - 1;
        var valueLocal = GetStackLocal(
            request.Context,
            value,
            CliValueKind.ManagedReference);
        var caller = request.Header.MethodInstance?.CanonicalName;
        var key = caller is null
            ? string.Empty
            : $"{caller}@{request.Instruction.Offset:x8}";
        if (caller is null || !request.Target.TypeTestSites.TryGetValue(key, out var site))
        {
            EmitRuntimeTest(
                code,
                request.Instruction,
                request.Header.MethodInstance,
                valueLocal,
                returnNullOnFailure, request.Context.ManagedThrowFunction);
            return;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        EmitReferenceEqualZero(code);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        var runs = Runs(site.MatchingTypes);
        if (runs.Count == 0)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        }
        else
        {
            var typeId = request.Context.NumericTemporaryI4Second;
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)typeId)));
            EmitMembership(code, typeId, runs, 0, runs.Count);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitFailedTest(code, valueLocal, returnNullOnFailure, request.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    // The matching type ids as runs of consecutive ids, in order.
    private List<(int First, int Last)> Runs(ImmutableArray<CliTypeIdentity> matchingTypes)
    {
        var runs = new List<(int First, int Last)>();
        foreach (var id in matchingTypes.Select(type => typeLayouts.GetObjectLayout(type).TypeId).Distinct().Order())
        {
            if (runs.Count != 0 && runs[^1].Last + 1 == id)
            {
                runs[^1] = (runs[^1].First, id);
            }
            else
            {
                runs.Add((id, id));
            }
        }
        return runs;
    }

    // An interface or a widely derived class matches many types, so a test
    // against each one ran every comparison. A few runs are still tested
    // together; more are searched, halving them at each branch, so a test
    // takes a branch per halving and one range check.
    private const int LinearRuns = 4;

    private static void EmitMembership(
        IWasmInstructionWriter code, int typeId, List<(int First, int Last)> runs, int start, int end)
    {
        if (end - start <= LinearRuns)
        {
            for (var index = start; index < end; index++)
            {
                var (first, last) = runs[index];
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)typeId)));
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(first)));
                if (first == last)
                {
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
                }
                else
                {
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(last - first + 1)));
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
                }
                if (index != start)
                {
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
                }
            }
            return;
        }
        var middle = start + (end - start) / 2;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)typeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(runs[middle].First)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(0x7f)));
        EmitMembership(code, typeId, runs, start, middle);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        EmitMembership(code, typeId, runs, middle, end);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitRuntimeTest(
        IWasmInstructionWriter code,
        CilInstruction instruction,
        MethodInstanceModel? method,
        int valueLocal,
        bool returnNullOnFailure,
        OptionalFunctionIndex throwFunction)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        EmitReferenceEqualZero(code);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        var target = types.Resolve(instruction, method);
        var membershipTarget = nullableTypes.Resolve(target) ?? target;
        var targetTypeId = typeLayouts.GetObjectLayout(membershipTarget).TypeId;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(targetTypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.IsAssignable)))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitFailedTest(code, valueLocal, returnNullOnFailure, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitFailedTest(
        IWasmInstructionWriter code,
        int valueLocal,
        bool returnNullOnFailure,
        OptionalFunctionIndex throwFunction)
    {
        if (returnNullOnFailure)
        {
            addresses.Emit(code, 0);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        }
        else
        {
            exceptions.Emit(code, ManagedExceptionKind.InvalidCast, throwFunction);
        }
    }

    private void EmitReferenceEqualZero(IWasmInstructionWriter code)
    {
        addresses.Emit(code, AddressOperation.EqualZero);
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);
}
