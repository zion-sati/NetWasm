// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;
using NetWasm.Compiler.Wasm.Emission.Planning;

public sealed class StaticDataElementInstructionEmitterTests
{
    [Fact]
    public void OwnsTheStaticDataElementCommand()
    {
        var fixture = new Fixture(WasmTarget.Wasm32, "u4", CliValueKind.I4, 4);

        var command = Assert.Single(fixture.Emitter.Commands);

        Assert.Equal(CilOperation.LoadStaticDataElement, command.Operation);
        Assert.Equal(InstructionFamily.ArraysFieldsStatics, command.Family);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, "u4", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm64, "u4", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm32, "i8", CliValueKind.I8, 8)]
    [InlineData(WasmTarget.Wasm32, "f4", CliValueKind.F4, 4)]
    [InlineData(WasmTarget.Wasm32, "f8", CliValueKind.F8, 8)]
    public void CheckedIndexedLoadReplacesTheIndexWithTheScalar(
        WasmTarget target,
        string primitive,
        CliValueKind kind,
        int size)
    {
        var fixture = new Fixture(target, primitive, kind, size);
        var request = fixture.Request([CliValueKind.I4]);

        fixture.Emitter.Emit(request);

        Assert.Equal([kind], request.Stack);
        Assert.Equal(
            [ManagedExceptionKind.IndexOutOfRange],
            fixture.Exceptions.Kinds);
        var code = GetCodeBytes(request);
        Assert.Contains(WasmOpcodes.I32LessThanUnsigned, code);
        Assert.Contains(WasmOpcodes.Else, code);
        Assert.Contains(target == WasmTarget.Wasm64
            ? WasmOpcodes.I64Multiply
            : WasmOpcodes.I32Multiply, code);
        if (target == WasmTarget.Wasm64)
        {
            Assert.Contains(WasmOpcodes.I64ExtendI32Unsigned, code);
            Assert.Contains(WasmOpcodes.I64Add, code);
        }
        else
        {
            Assert.Contains(WasmOpcodes.I32Add, code);
        }
    }

    [Fact]
    public void MissingMetadataIsRejected()
    {
        var fixture = new Fixture(WasmTarget.Wasm32, "u4", CliValueKind.I4, 4);
        var request = fixture.Request([CliValueKind.I4], new CilOperand.None());

        Assert.Throws<CompilerException>(() => fixture.Emitter.Emit(request));
    }

    [Theory]
    [InlineData(InvalidFixture.WrongStack)]
    [InlineData(InvalidFixture.EmptyData)]
    [InlineData(InvalidFixture.SizeMismatch)]
    [InlineData(InvalidFixture.MisalignedData)]
    [InlineData(InvalidFixture.NonScalar)]
    public void InvalidLoweringContractIsRejected(InvalidFixture invalid)
    {
        var kind = invalid == InvalidFixture.NonScalar
            ? CliValueKind.ValueType
            : CliValueKind.I4;
        var fixture = new Fixture(
            WasmTarget.Wasm32,
            invalid == InvalidFixture.NonScalar ? "value" : "u4",
            kind,
            4,
            address: invalid == InvalidFixture.MisalignedData ? 202 : 200,
            elementCount: invalid switch
            {
                InvalidFixture.EmptyData => 0,
                InvalidFixture.SizeMismatch => 3,
                _ => 2,
            });
        var request = fixture.Request(
            invalid == InvalidFixture.WrongStack
                ? []
                : [CliValueKind.I4]);

        Assert.Throws<CompilerException>(() => fixture.Emitter.Emit(request));
    }

    public enum InvalidFixture
    {
        WrongStack,
        EmptyData,
        SizeMismatch,
        MisalignedData,
        NonScalar,
    }

    private sealed class Fixture
    {
        private readonly DataLayouts _layouts;
        private readonly CilOperand.ReadOnlySpanData _data;

        public Fixture(
            WasmTarget target,
            string primitive,
            CliValueKind kind,
            int size,
            int address = 200,
            int elementCount = 2)
        {
            _layouts = new(WasmTargetLayout.For(target), size, address);
            Exceptions = new();
            Emitter = new(
                _layouts,
                _layouts,
                _layouts,
                new AddressInstructionEmitter(_layouts),
                Exceptions);
            var assembly = new AssemblyIdentity("fixture");
            var element = primitive == "value"
                ? CliTypeIdentity.Named(
                    assembly,
                    "Fixture",
                    "Value",
                    isValueType: true)
                : CliTypeIdentity.Primitive(primitive, kind);
            var span = CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(
                    assembly,
                    "System",
                    "ReadOnlySpan`1",
                    isValueType: true),
                [element]);
            var dataType = CliTypeIdentity.Named(
                assembly,
                "",
                $"__StaticArrayInitTypeSize{size * 2}",
                isValueType: true);
            var field = new FieldDefinitionModel(
                new(assembly, 1),
                new(assembly, 2),
                "Data",
                dataType,
                true)
            {
                InitialData = [.. new byte[size * 2]],
            };
            _data = new(
                new(field, CliTypeIdentity.Named(
                    assembly,
                    "Fixture",
                    "Owner",
                    isValueType: false), dataType),
                span,
                element,
                elementCount);
        }

        public RecordingExceptions Exceptions { get; }
        public StaticDataElementInstructionEmitter Emitter { get; }

        public InstructionEmissionRequest Request(
            IEnumerable<CliValueKind> stack,
            CilOperand? operand = null) => CreateInstructionRequest(
                CilOperation.LoadStaticDataElement,
                stack,
                operand ?? _data,
                CreateMethodEmissionContext(),
                maxStack: 3);
    }

    private sealed class DataLayouts(
        WasmTargetLayout target,
        int size,
        int address) :
        ITargetLayout,
        IValueLayoutProvider,
        IStaticFieldLayoutProvider
    {
        public WasmTargetLayout Target { get; } = target;

        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            new(type, size, size, []);

        public StaticFieldLayout GetStaticFieldLayout(EntityKey field) =>
            new(address);

        public StaticFieldLayout GetStaticFieldLayout(FieldInstanceModel field) =>
            new(address);
    }

    private sealed class RecordingExceptions : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(
            IWasmInstructionWriter code,
            ManagedExceptionKind kind,
            OptionalFunctionIndex throwFunctionIndex) => Kinds.Add(kind);
    }
}
