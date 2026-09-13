using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class EnumTypeArgumentValidatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidatesNullAndRequiresARepresentedEnumType(bool hasEnums)
    {
        var exceptions = new RecordingExceptions();
        var code = new RecordingWriter();
        ITypeObjectIdReader typeIds = new TypeObjectIdReader(
            new RecordingReferences(),
            exceptions,
            new Layouts());
        IEnumTypeArgumentValidator validator = AsValidator(new EnumTypeArgumentValidator(
            new Metadata(hasEnums
                ? [Entry(7), Entry(11)]
                : []),
            typeIds,
            exceptions));

        validator.Validate(code, 3, 5);

        Assert.Equal(
            [ManagedExceptionKind.ArgumentNull, ManagedExceptionKind.Argument],
            exceptions.Kinds);
        Assert.Contains(code.Instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Load &&
            instruction.Operand.Offset == WasmTargetLayout.Wasm32.ObjectHeaderSize);
        Assert.Equal(
            hasEnums ? 2 : 0,
            code.Instructions.Count(instruction =>
                instruction.Opcode == WasmOpcodes.I32Or));
    }

    private static IEnumTypeArgumentValidator AsValidator(
        EnumTypeArgumentValidator validator) =>
        new[] { validator }.Cast<IEnumTypeArgumentValidator>().Single();

    private static EnumMetadataLayout Entry(int typeId) => new(
        new EntityKey(new AssemblyIdentity("EnumTypeArgumentValidatorTests"), typeId),
        typeId,
        0,
        CliTypeIdentity.Primitive("i4", CliValueKind.I4),
        false,
        []);

    private sealed class Metadata(ImmutableArray<EnumMetadataLayout> entries) :
        IEnumMetadataSource
    {
        public ImmutableArray<EnumMetadataLayout> EnumMetadata => entries;
    }

    private sealed class Layouts : ITargetLayout
    {
        public WasmTargetLayout Target => WasmTargetLayout.Wasm32;
    }

    private sealed class RecordingReferences : IReferenceComparisonEmitter
    {
        public void Emit(
            IWasmInstructionWriter code,
            ReferenceComparison comparison,
            CliValueKind operandType = CliValueKind.ManagedReference) =>
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
    }

    private sealed class RecordingExceptions : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind) =>
            Kinds.Add(kind);
    }

    private sealed class RecordingWriter : IWasmInstructionWriter
    {
        public List<WasmInstruction> Instructions { get; } = [];

        public void Write(WasmInstruction instruction) => Instructions.Add(instruction);
    }
}
