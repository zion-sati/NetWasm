using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class AtomicInstructionEmitterTests
{
    public static TheoryData<WasmTarget, CliValueKind> SupportedValues
    {
        get
        {
            var rows = new TheoryData<WasmTarget, CliValueKind>();
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
                foreach (var kind in new[]
                {
                CliValueKind.I4, CliValueKind.I8, CliValueKind.NativeInt,
                CliValueKind.ManagedReference, CliValueKind.ManagedAddress,
            })
                {
                    rows.Add(target, kind);
                }
            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(SupportedValues))]
    public void CompareExchangeChecksTheAddressBeforeLoadingAndPreservesLowerStackValues(
        WasmTarget target,
        CliValueKind kind)
    {
        var layout = WasmTargetLayout.For(target);
        var exceptions = new RecordingExceptions();
        var addresses = new RecordingAddresses(layout);
        var emitter = ThroughContract(new AtomicInstructionEmitter(
            new RecordingLayoutProvider(layout),
            CreateTypeOperands(new FakeProgram()),
            exceptions,
            addresses));
        var identity = kind == CliValueKind.ManagedReference
            ? CliTypeIdentity.Named(new("Tests"), "Tests", "Reference", isValueType: false)
            : CliTypeIdentity.FromStackKind(kind);
        var request = CreateInstructionRequest(
            CilOperation.CompareExchange,
            [CliValueKind.ManagedReference, CliValueKind.ManagedAddress, kind, kind],
            new CilOperand.TypeIdentity(identity),
            maxStack: 4);

        var command = Assert.Single(emitter.Commands);
        Assert.Equal(CilOperation.CompareExchange, command.Operation);
        Assert.Equal(InstructionFamily.ArraysFieldsStatics, command.Family);
        command.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());

        Assert.Equal([CliValueKind.ManagedReference, kind], request.Stack);
        Assert.Equal([ManagedExceptionKind.NullReference], exceptions.Kinds);
        Assert.Equal([AddressOperation.EqualZero], addresses.Operations);
        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions();
        var wideValue = kind == CliValueKind.I8 || layout.UsesMemory64 && kind != CliValueKind.I4;
        Assert.Equal(
            [
                WasmOpcodes.LocalGet,
                layout.UsesMemory64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero,
                WasmOpcodes.If, WasmOpcodes.Unreachable, WasmOpcodes.End,
                WasmOpcodes.LocalGet,
                wideValue ? WasmOpcodes.I64Load : WasmOpcodes.I32Load,
                WasmOpcodes.LocalSet, WasmOpcodes.LocalGet, WasmOpcodes.LocalGet,
                wideValue ? WasmOpcodes.I64Equal : WasmOpcodes.I32Equal,
                WasmOpcodes.If, WasmOpcodes.LocalGet, WasmOpcodes.LocalGet,
                wideValue ? WasmOpcodes.I64Store : WasmOpcodes.I32Store,
                WasmOpcodes.End, WasmOpcodes.LocalGet, WasmOpcodes.LocalSet,
            ],
            instructions.Select(instruction => instruction.Opcode));
        Assert.Equal(Local(1, CliValueKind.ManagedAddress), instructions[0].Operand.UnsignedValue);
        Assert.Equal(instructions[0], instructions[5]);
        Assert.Equal(instructions[0], instructions[12]);
        Assert.Equal(Local(3, kind), instructions[9].Operand.UnsignedValue);
        Assert.Equal(Local(2, kind), instructions[13].Operand.UnsignedValue);
        Assert.Equal(Local(1, kind), instructions[^1].Operand.UnsignedValue);
        var temporary = (uint)(wideValue
            ? request.Context.NumericTemporaryI8
            : request.Context.NumericTemporaryI4);
        Assert.Equal(temporary, instructions[7].Operand.UnsignedValue);
        Assert.Equal(temporary, instructions[8].Operand.UnsignedValue);
        Assert.Equal(temporary, instructions[16].Operand.UnsignedValue);

        uint Local(int slot, CliValueKind valueKind) => (uint)WasmLocalLayoutPlanner
            .GetEvaluationStackLocal(request.Context.StackLocals, slot, valueKind, layout);
    }

    [Theory]
    [InlineData(CliValueKind.F4)]
    [InlineData(CliValueKind.F8)]
    [InlineData(CliValueKind.ValueType)]
    public void CompareExchangeRejectsUnsupportedValueKindsBeforeEmission(CliValueKind kind)
    {
        var exceptions = new RecordingExceptions();
        var addresses = new RecordingAddresses(WasmTargetLayout.Wasm32);
        var emitter = ThroughContract(new AtomicInstructionEmitter(
            new RecordingLayoutProvider(),
            CreateTypeOperands(new FakeProgram()),
            exceptions,
            addresses));
        var request = CreateInstructionRequest(
            CilOperation.CompareExchange,
            [CliValueKind.ManagedAddress, kind, kind],
            new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(kind)));

        var exception = Assert.Throws<CompilerException>(() => Assert.Single(emitter.Commands)
            .Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver()));

        Assert.Equal(DiagnosticCode.UnsupportedCil, exception.Diagnostic.Code);
        Assert.Empty(GetCodeBytes(request));
        Assert.Empty(exceptions.Kinds);
        Assert.Empty(addresses.Operations);
        Assert.Equal([CliValueKind.ManagedAddress, kind, kind], request.Stack);
    }

    private static IInstructionCommandProvider ThroughContract(IInstructionCommandProvider provider) => provider;

    private sealed class RecordingExceptions : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind)
        {
            Kinds.Add(kind);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        }
    }

    private sealed class RecordingAddresses(WasmTargetLayout target) : IAddressInstructionEmitter
    {
        public List<AddressOperation> Operations { get; } = [];

        public void Emit(IWasmInstructionWriter code, AddressOperation operation)
        {
            Operations.Add(operation);
            Assert.Equal(AddressOperation.EqualZero, operation);
            code.Write(WasmInstruction.NoOperand(target.UsesMemory64
                ? WasmOpcodes.I64EqualZero
                : WasmOpcodes.I32EqualZero));
        }

        public void Emit(IWasmInstructionWriter code, int constant) =>
            throw new InvalidOperationException("No address constant is needed.");
    }
}
