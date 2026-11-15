// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ReadOnlySpanInstructionEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, 0)]
    [InlineData(WasmTarget.Wasm32, 16)]
    [InlineData(WasmTarget.Wasm64, 0)]
    [InlineData(WasmTarget.Wasm64, 16)]
    public void CreatesSpanWithStaticDataPointerAndElementCount(WasmTarget target, int offset)
    {
        var fixture = new Fixture(target);
        fixture.Reference = fixture.Reference with
        {
            SignatureType = CliTypeIdentity.Modified(fixture.Reference.SignatureType,
                CliTypeIdentity.Named(new("tests"), "Tests", "ReadOnly", false), true),
        };
        var request = fixture.Request(offset);
        var writer = new RecordingInstructionWriter();

        fixture.Emitter().Emit(request, writer);

        Assert.Equal(CliValueKind.ValueType, Assert.Single(request.Stack));
        Assert.Equal(offset == 0 ? [256] : new[] { offset, 256 }, fixture.Addresses.Constants.ToArray());
        Assert.Equal(offset == 0 ? 0 : 1, fixture.Addresses.Additions);
        var code = writer.ToInstructions();
        Assert.Contains(code, item => item.Opcode == (target == WasmTarget.Wasm32
            ? WasmOpcodes.I32Store : WasmOpcodes.I64Store) && item.Operand.Offset == 0);
        Assert.Contains(code, item => item.Opcode == WasmOpcodes.I32Store &&
            item.Operand.Offset == (uint)fixture.Target.AddressSize);
        Assert.Contains(code, item => item.Opcode == WasmOpcodes.I32Constant && item.Operand.SignedValue == 2);
        Assert.Equal(WasmOpcodes.LocalSet, code[^1].Opcode);
    }

    [Theory]
    [MemberData(nameof(InvalidContracts))]
    public void RejectsInvalidContractsBeforeOutputOrStackMutation(WasmTarget target, string invalid)
    {
        var fixture = new Fixture(target);
        switch (invalid)
        {
            case "count": fixture.Count = 0; break;
            case "data-size": fixture.Count = 1; break;
            case "alignment": fixture.StaticAddress = 257; break;
            case "reference-size": fixture.ReferenceLayout = fixture.ReferenceLayout with { Size = 1 }; break;
            case "length-size": fixture.LengthLayout = fixture.LengthLayout with { Size = 1 }; break;
            case "reference-negative": fixture.ReferenceLayout = fixture.ReferenceLayout with { Offset = -1 }; break;
            case "reference-outside": fixture.ReferenceLayout = fixture.ReferenceLayout with { Offset = 64 }; break;
            case "length-negative": fixture.LengthLayout = fixture.LengthLayout with { Offset = -1 }; break;
            case "length-outside": fixture.LengthLayout = fixture.LengthLayout with { Offset = 64 }; break;
            case "byref-map": fixture.ByRefs = []; break;
            case "reference-missing": fixture.Reference = fixture.Reference with { Name = "other" }; break;
            case "length-missing": fixture.Length = fixture.Length with { Name = "other" }; break;
            case "reference-kind": fixture.Reference = fixture.Reference with { SignatureType = Fixture.Scalar }; break;
            case "element":
                fixture.Reference = fixture.Reference with
                { SignatureType = CliTypeIdentity.ManagedByReference(CliTypeIdentity.FromStackKind(CliValueKind.I8)) }; break;
            case "length-kind":
                fixture.Length = fixture.Length with
                { SignatureType = CliTypeIdentity.FromStackKind(CliValueKind.I8) }; break;
            case "metadata": break;
            default: throw new ArgumentOutOfRangeException(nameof(invalid));
        }
        var request = fixture.Request(0);
        if (invalid == "metadata")
            request = request with { Instruction = request.Instruction with { Operand = new CilOperand.None() } };
        var writer = new RecordingInstructionWriter();

        var error = Assert.Throws<CompilerException>(() => fixture.Emitter().Emit(request, writer));

        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
        Assert.Empty(writer.ToInstructions());
        Assert.Empty(request.Stack);
        Assert.Empty(fixture.Addresses.Constants);
        Assert.Equal(0, fixture.Addresses.Additions);
    }

    public static TheoryData<WasmTarget, string> InvalidContracts()
    {
        var data = new TheoryData<WasmTarget, string>();
        string[] cases = ["count", "data-size", "alignment", "reference-size", "length-size",
            "reference-negative", "reference-outside", "length-negative", "length-outside", "byref-map",
            "reference-missing", "length-missing", "reference-kind", "element", "length-kind", "metadata"];
        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
            foreach (var invalid in cases) data.Add(target, invalid);
        return data;
    }

    private sealed class Fixture : ITypeDefinitionResolver, IFieldRepository, ITargetLayout,
        IValueLayoutProvider, IInstanceFieldLayoutProvider, IStaticFieldLayoutProvider
    {
        public static readonly CliTypeIdentity Scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        private static readonly EntityKey Owner = new(new("span-tests"), 0x02000001);
        private static readonly CliTypeIdentity Span = CliTypeIdentity.Named(Owner.Assembly, "Tests", "Span", true);
        public WasmTargetLayout Target { get; }
        public FieldDefinitionModel Reference { get; set; } = new(new(Owner.Assembly, 0x04000001), Owner,
            "_reference", CliTypeIdentity.ManagedByReference(Scalar), false);
        public FieldDefinitionModel Length { get; set; } = new(new(Owner.Assembly, 0x04000002), Owner, "_length", Scalar, false);
        public FieldLayout ReferenceLayout { get; set; }
        public FieldLayout LengthLayout { get; set; }
        public ImmutableArray<int> ByRefs { get; set; } = [0];
        public int Count { get; set; } = 2;
        public int StaticAddress { get; set; } = 256;
        public Addresses Addresses { get; } = new();

        public Fixture(WasmTarget target)
        {
            Target = WasmTargetLayout.For(target);
            ReferenceLayout = new(0) { Size = Target.AddressSize };
            LengthLayout = new(Target.AddressSize) { Size = 4 };
        }

        public ReadOnlySpanInstructionEmitter Emitter() => new(this, this, this, this, this, this, Addresses);

        public InstructionEmissionRequest Request(int offset)
        {
            var context = CreateMethodEmissionContext();
            context = context with
            {
                ValueLayout = context.ValueLayout with
                {
                    TemporaryOffsets =
                ImmutableDictionary<int, int>.Empty.Add(0, offset)
                }
            };
            var field = new FieldDefinitionModel(new(Owner.Assembly, 0x04000003), Owner, "Data", Scalar, true)
            { InitialData = [1, 0, 0, 0, 2, 0, 0, 0] };
            return CreateInstructionRequest(CilOperation.CreateReadOnlySpanFromRva,
                operand: new CilOperand.ReadOnlySpanData(new(field, Span, Scalar), Span, Scalar, Count), context: context);
        }

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(Owner, "Tests", "Span", true, [Reference.Key, Length.Key], []);
        public FieldDefinitionModel GetField(EntityKey key) => key == Reference.Key ? Reference : Length;
        public ValueLayout GetValueLayout(CliTypeIdentity type) => type.Equals(Span)
            ? new(type, Target.AddressSize + 4, Target.AddressSize, []) { ByReferenceOffsets = ByRefs }
            : new(type, 4, 4, []);
        public FieldLayout GetFieldLayout(EntityKey field) => field == Reference.Key ? ReferenceLayout : LengthLayout;
        public FieldLayout GetFieldLayout(FieldInstanceModel field) => GetFieldLayout(field.Definition.Key);
        public StaticFieldLayout GetStaticFieldLayout(EntityKey field) => new(StaticAddress);
        public StaticFieldLayout GetStaticFieldLayout(FieldInstanceModel field) => new(StaticAddress);
    }

    private sealed class Addresses : IAddressInstructionEmitter
    {
        public List<int> Constants { get; } = [];
        public int Additions { get; private set; }
        public void Emit(IWasmInstructionWriter code, int constant) => Constants.Add(constant);
        public void Emit(IWasmInstructionWriter code, AddressOperation operation)
        {
            Assert.Equal(AddressOperation.Add, operation);
            Additions++;
        }
    }
}
