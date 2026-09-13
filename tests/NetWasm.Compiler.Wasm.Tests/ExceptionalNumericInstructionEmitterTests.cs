using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ExceptionalNumericInstructionEmitterTests
{
    public static TheoryData<CilOperation, WasmTarget, bool> MixedGuardedOperands
    {
        get
        {
            var cases = new TheoryData<CilOperation, WasmTarget, bool>();
            foreach (var operation in new[]
            {
                CilOperation.Divide, CilOperation.DivideUnsigned,
                CilOperation.Remainder, CilOperation.RemainderUnsigned,
            })
            {
                foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
                {
                    cases.Add(operation, target, false);
                    cases.Add(operation, target, true);
                }
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(MixedGuardedOperands))]
    public void MixedArithmeticGuardsReadOnlyAvailableOperandStorage(
        CilOperation operation,
        WasmTarget target,
        bool nativeLeft)
    {
        var request = CreateRequest(operation,
            nativeLeft ? CliValueKind.NativeInt : CliValueKind.I4,
            nativeLeft ? CliValueKind.I4 : CliValueKind.NativeInt);
        var layout = WasmTargetLayout.For(target);
        var available = request.Stack.Select((kind, slot) =>
            WasmLocalLayoutPlanner.GetEvaluationStackLocal(request.Context.StackLocals, slot, kind, layout))
            .ToHashSet();

        Emit(CreateEmitter(target), request);

        var writer = (RecordingInstructionWriter)GetCodeWriter(request);
        foreach (var instruction in writer.ToInstructions())
        {
            var local = checked((int)instruction.Operand.UnsignedValue);
            if (local < request.Context.StackLocals.I4Base || local >= request.Context.StackLocals.End)
            {
                continue;
            }
            if (instruction.Opcode == WasmOpcodes.LocalGet)
            {
                Assert.Contains(local, available);
            }
            else if (instruction.Opcode is WasmOpcodes.LocalSet or WasmOpcodes.LocalTee)
            {
                available.Add(local);
            }
        }
        Assert.Equal([CliValueKind.NativeInt], request.Stack);
    }

    public static TheoryData<CilOperation, WasmTarget, bool> MixedCheckedOperands
    {
        get
        {
            var cases = new TheoryData<CilOperation, WasmTarget, bool>();
            foreach (var operation in new[]
            {
                CilOperation.AddChecked, CilOperation.AddCheckedUnsigned,
                CilOperation.SubtractChecked, CilOperation.SubtractCheckedUnsigned,
                CilOperation.MultiplyChecked, CilOperation.MultiplyCheckedUnsigned,
            })
            {
                foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
                {
                    cases.Add(operation, target, false);
                    cases.Add(operation, target, true);
                }
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(MixedCheckedOperands))]
    public void MixedCheckedOperandsReachTheArithmeticContractInNativeStorage(
        CilOperation operation,
        WasmTarget target,
        bool nativeLeft)
    {
        var checkedBinary = new RecordingCheckedBinaryEmitter();
        var request = CreateRequest(operation,
            nativeLeft ? CliValueKind.NativeInt : CliValueKind.I4,
            nativeLeft ? CliValueKind.I4 : CliValueKind.NativeInt);
        var layout = WasmTargetLayout.For(target);
        var leftLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            request.Context.StackLocals, 0, CliValueKind.NativeInt, layout);
        var rightLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            request.Context.StackLocals, 1, CliValueKind.NativeInt, layout);

        Emit(CreateEmitter(target, checkedBinary), request);

        Assert.Equal([CliValueKind.NativeInt], request.Stack);
        Assert.Equal(CliValueKind.NativeInt, checkedBinary.Type);
        Assert.Equal(leftLocal, checkedBinary.LeftLocal);
        Assert.Equal(rightLocal, checkedBinary.RightLocal);
        if (target == WasmTarget.Wasm64)
        {
            var sourceSlot = nativeLeft ? 1 : 0;
            var sourceLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
                request.Context.StackLocals, sourceSlot, CliValueKind.I4, layout);
            var destinationLocal = nativeLeft ? rightLocal : leftLocal;
            Assert.Contains(checkedBinary.Prefix, instruction =>
                instruction.Opcode == WasmOpcodes.LocalGet &&
                instruction.Operand.UnsignedValue == sourceLocal);
            Assert.Contains(checkedBinary.Prefix, instruction =>
                instruction.Opcode == WasmOpcodes.LocalSet &&
                instruction.Operand.UnsignedValue == destinationLocal);
            var unsigned = operation is CilOperation.AddCheckedUnsigned or
                CilOperation.SubtractCheckedUnsigned or CilOperation.MultiplyCheckedUnsigned;
            Assert.Contains(checkedBinary.Prefix, instruction => instruction.Opcode ==
                (unsigned ? WasmOpcodes.I64ExtendI32Unsigned : WasmOpcodes.I64ExtendI32Signed));
        }
    }

    [Theory]
    [InlineData(CilOperation.Divide, WasmOpcodes.I32DivideSigned)]
    [InlineData(CilOperation.DivideUnsigned, WasmOpcodes.I32DivideUnsigned)]
    [InlineData(CilOperation.Remainder, WasmOpcodes.I32RemainderSigned)]
    [InlineData(CilOperation.RemainderUnsigned, WasmOpcodes.I32RemainderUnsigned)]
    public void IntegerDivisionFamilyEmitsGuardAndOperation(
        CilOperation operation,
        byte expectedOpcode)
    {
        var exceptions = new RecordingImplicitExceptionEmitter();
        var request = CreateRequest(operation, CliValueKind.I4, CliValueKind.I4);

        Emit(CreateEmitter(exceptions: exceptions), request);

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.Contains(WasmOpcodes.If, GetCodeBytes(request));
        Assert.Contains(expectedOpcode, GetCodeBytes(request));
        Assert.Contains(ManagedExceptionKind.DivideByZero, exceptions.Kinds);
    }

    [Theory]
    [InlineData(CilOperation.AddChecked, CliValueKind.I4, WasmTarget.Wasm32)]
    [InlineData(CilOperation.SubtractChecked, CliValueKind.I8, WasmTarget.Wasm64)]
    [InlineData(CilOperation.MultiplyChecked, CliValueKind.NativeInt, WasmTarget.Wasm64)]
    [InlineData(CilOperation.AddCheckedUnsigned, CliValueKind.I4, WasmTarget.Wasm32)]
    [InlineData(CilOperation.SubtractCheckedUnsigned, CliValueKind.NativeInt, WasmTarget.Wasm32)]
    [InlineData(CilOperation.MultiplyCheckedUnsigned, CliValueKind.I8, WasmTarget.Wasm64)]
    public void CheckedBinaryOperationsDelegateAndStoreTheirResult(
        CilOperation operation,
        CliValueKind type,
        WasmTarget target)
    {
        var checkedBinary = new RecordingCheckedBinaryEmitter();
        var request = CreateRequest(operation, type, type);

        Emit(CreateEmitter(target, checkedBinary), request);

        Assert.Equal([type], request.Stack);
        Assert.Equal(1, checkedBinary.CallCount);
        Assert.Equal(operation, checkedBinary.Operation);
        Assert.Equal(type, checkedBinary.Type);
    }

    [Theory]
    [InlineData(CilOperation.Divide, CliValueKind.I8, WasmTarget.Wasm64, WasmOpcodes.I64DivideSigned)]
    [InlineData(CilOperation.DivideUnsigned, CliValueKind.I8, WasmTarget.Wasm64, WasmOpcodes.I64DivideUnsigned)]
    [InlineData(CilOperation.Divide, CliValueKind.NativeInt, WasmTarget.Wasm32, WasmOpcodes.I32DivideSigned)]
    [InlineData(CilOperation.DivideUnsigned, CliValueKind.NativeInt, WasmTarget.Wasm64, WasmOpcodes.I64DivideUnsigned)]
    public void IntegerDivisionSupportsAllAddressWidths(
        CilOperation operation,
        CliValueKind type,
        WasmTarget target,
        byte expectedOpcode)
    {
        var exceptions = new RecordingImplicitExceptionEmitter();
        var request = CreateRequest(operation, type, type);

        Emit(CreateEmitter(target, exceptions: exceptions), request);

        Assert.Equal([type], request.Stack);
        Assert.Contains(expectedOpcode, GetCodeBytes(request));
        Assert.Contains(ManagedExceptionKind.DivideByZero, exceptions.Kinds);
        if (operation == CilOperation.Divide)
        {
            Assert.Contains(ManagedExceptionKind.Overflow, exceptions.Kinds);
        }
    }

    [Fact]
    public void NativeIntAndInt32DivisionWidensOnMemory64()
    {
        var request = CreateRequest(
            CilOperation.DivideUnsigned,
            CliValueKind.NativeInt,
            CliValueKind.I4);

        Emit(CreateEmitter(WasmTarget.Wasm64), request);

        Assert.Equal([CliValueKind.NativeInt], request.Stack);
        Assert.Contains(WasmOpcodes.I64ExtendI32Signed, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.I64DivideUnsigned, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(CilOperation.Divide, CliValueKind.F4, WasmOpcodes.F32Divide)]
    [InlineData(CilOperation.Divide, CliValueKind.F8, WasmOpcodes.F64Divide)]
    public void FloatingDivisionSkipsIntegerGuards(
        CilOperation operation,
        CliValueKind type,
        byte expectedOpcode)
    {
        var exceptions = new RecordingImplicitExceptionEmitter();
        var request = CreateRequest(operation, type, type);

        Emit(CreateEmitter(exceptions: exceptions), request);

        Assert.Equal([type], request.Stack);
        Assert.Contains(expectedOpcode, GetCodeBytes(request));
        Assert.Empty(exceptions.Kinds);
    }

    [Theory]
    [InlineData(CliValueKind.F4, WasmOpcodes.F32Divide, WasmOpcodes.F32Truncate)]
    [InlineData(CliValueKind.F8, WasmOpcodes.F64Divide, WasmOpcodes.F64Truncate)]
    public void FloatingRemainderUsesTruncateAndMultiply(
        CliValueKind type,
        byte divisionOpcode,
        byte truncateOpcode)
    {
        var exceptions = new RecordingImplicitExceptionEmitter();
        var request = CreateRequest(CilOperation.Remainder, type, type);

        Emit(CreateEmitter(exceptions: exceptions), request);

        Assert.Equal([type], request.Stack);
        Assert.Contains(divisionOpcode, GetCodeBytes(request));
        Assert.Contains(truncateOpcode, GetCodeBytes(request));
        Assert.Empty(exceptions.Kinds);
    }

    [Theory]
    [InlineData(CilOperation.Remainder, CliValueKind.I8, WasmTarget.Wasm64, WasmOpcodes.I64RemainderSigned)]
    [InlineData(CilOperation.RemainderUnsigned, CliValueKind.NativeInt, WasmTarget.Wasm32, WasmOpcodes.I32RemainderUnsigned)]
    [InlineData(CilOperation.RemainderUnsigned, CliValueKind.NativeInt, WasmTarget.Wasm64, WasmOpcodes.I64RemainderUnsigned)]
    public void IntegerRemainderSupportsAllAddressWidths(
        CilOperation operation,
        CliValueKind type,
        WasmTarget target,
        byte expectedOpcode)
    {
        var exceptions = new RecordingImplicitExceptionEmitter();
        var request = CreateRequest(operation, type, type);

        Emit(CreateEmitter(target, exceptions: exceptions), request);

        Assert.Equal([type], request.Stack);
        Assert.Contains(expectedOpcode, GetCodeBytes(request));
        Assert.Contains(ManagedExceptionKind.DivideByZero, exceptions.Kinds);
    }

    private static IInstructionCommandProvider CreateEmitter(
        WasmTarget target = WasmTarget.Wasm32,
        ICheckedBinaryEmitter? checkedBinary = null,
        IImplicitExceptionEmitter? exceptions = null)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        return AsProvider(new ExceptionalNumericInstructionEmitter(
            layouts,
            checkedBinary ?? new RecordingCheckedBinaryEmitter(),
            exceptions ?? new RecordingImplicitExceptionEmitter()));
    }

    private static InstructionEmissionRequest CreateRequest(
        CilOperation operation,
        CliValueKind left,
        CliValueKind right) =>
        CreateInstructionRequest(operation, [left, right]);

    private static void Emit(
        IInstructionCommandProvider provider,
        InstructionEmissionRequest request)
    {
        var command = provider.Commands.SingleOrDefault(candidate =>
            candidate.Operation == request.Instruction.Operation) ??
            throw new InvalidOperationException(
                $"{provider.GetType().Name} does not own " +
                $"'{request.Instruction.Operation}'.");
        command.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());
    }

    private static IInstructionCommandProvider AsProvider(
        IInstructionCommandProvider provider) => provider;

    private sealed class RecordingCheckedBinaryEmitter : ICheckedBinaryEmitter
    {
        public int CallCount { get; private set; }
        public CilOperation Operation { get; private set; }
        public CliValueKind Type { get; private set; }
        public int LeftLocal { get; private set; }
        public int RightLocal { get; private set; }
        public ImmutableArray<WasmInstruction> Prefix { get; private set; } = [];

        public void Emit(
            IWasmInstructionWriter code,
            CilOperation operation,
            CliValueKind type,
            int leftLocal,
            int rightLocal,
            int resultLocal)
        {
            CallCount++;
            Operation = operation;
            Type = type;
            LeftLocal = leftLocal;
            RightLocal = rightLocal;
            Prefix = ((RecordingInstructionWriter)code).ToInstructions();
        }
    }

    private sealed class RecordingImplicitExceptionEmitter : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind)
        {
            Kinds.Add(kind);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Throw));
        }
    }
}
