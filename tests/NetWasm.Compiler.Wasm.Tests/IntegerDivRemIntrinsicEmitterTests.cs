// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;
using NetWasm.Compiler.Wasm.Emission.Planning;

public sealed class IntegerDivRemIntrinsicEmitterTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void RejectsIncompatibleSignatureBeforeWritingInstructions(int shape)
    {
        var element = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
        var other = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        if (shape == 7)
            element = CliTypeIdentity.Primitive("r8", CliValueKind.F8);
        var tuple = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", "ValueTuple`2", true),
            shape == 4 ? [element] : shape == 5 ? [other, element] :
            shape == 6 ? [element, other] : [element, element]);
        var signature = MethodSignatureModel.Create(shape == 1 ? element : tuple,
            shape == 2 ? [element] : shape == 3 ? [element, other] : [element, element]);
        var instruction = CreateInstructionRequest(CilOperation.Call,
            [element.StackKind, element.StackKind], maxStack: 2);
        var definition = new MethodDefinitionModel(Key(0x06000100), TypeKey, "DivRem", true, signature, 1);
        var method = new MethodInstanceModel(definition,
            CliTypeIdentity.Named(Assembly, "System", "Math", false), [], signature);
        var request = new RuntimeIntrinsicEmissionRequest(
            new CallEmissionRequest(instruction, method, 0, shape == 0 ? 1 : 2),
            RuntimeIntrinsic.IntegerDivRem, null, WasmTargetLayout.Wasm32, CreateFunctionIndexResolver());
        var layouts = new RecordingLayoutProvider();
        var exceptions = new RecordingImplicitExceptionEmitter();
        var code = GetCodeWriter(instruction);
        var emitter = new IntegerDivRemIntrinsicEmitter(layouts,
            new DivRemLayouts(tuple, element, shape == 8 ? 24 : 16),
            new ValueFrameAddressEmitter(layouts), exceptions);

        Assert.Throws<InvalidOperationException>(() => emitter.Emit(request, code));

        Assert.Empty(((RecordingInstructionWriter)code).ToInstructions());
        Assert.Empty(exceptions.Kinds);
    }

    public static IEnumerable<object[]> IntegralCases()
    {
        foreach (var primitive in new[] { "i1", "u1", "i2", "u2", "char", "i4", "u4", "i8", "u8", "nativeint", "nativeuint" })
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
                foreach (var promoted in new[] { false, true })
                    yield return [primitive, target, promoted];
    }

    [Theory]
    [MemberData(nameof(IntegralCases))]
    public void IntegralResultsPreserveWidthSignednessAndSelectedStorage(
        string primitive, WasmTarget target, bool promoted)
    {
        var targetLayout = target == WasmTarget.Wasm32 ? WasmTargetLayout.Wasm32 : WasmTargetLayout.Wasm64;
        var kind = primitive switch
        {
            "i8" or "u8" => CliValueKind.I8,
            "nativeint" or "nativeuint" => CliValueKind.NativeInt,
            _ => CliValueKind.I4,
        };
        var size = primitive switch
        {
            "i1" or "u1" => 1,
            "i2" or "u2" or "char" => 2,
            "i8" or "u8" => 8,
            "nativeint" or "nativeuint" => targetLayout.AddressSize,
            _ => 4,
        };
        var element = CliTypeIdentity.Primitive(primitive, kind);
        var tuple = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", "ValueTuple`2", true),
            [element, element]);
        var signature = MethodSignatureModel.Create(tuple, element, element);
        var definition = new MethodDefinitionModel(
            Key(0x06000100),
            TypeKey,
            "DivRem",
            true,
            signature,
            1);
        var method = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(Assembly, "System", "Math", false),
            [],
            signature);
        var plan = new TransientAggregatePromotionPlan(
            ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(0, element),
            [],
            [],
            []);
        var context = CreateMethodEmissionContext(2) with
        {
            TransientAggregatePromotions = new(
                plan,
                ImmutableDictionary<int, TransientAggregateScalarLocals>.Empty.Add(
                    0,
                    new(40, 41))),
        };
        if (!promoted)
        {
            context = context with
            {
                TransientAggregatePromotions = TransientAggregatePromotionLayout.Empty,
                ValueLayout = context.ValueLayout with
                {
                    Size = size * 2,
                    TemporaryOffsets = ImmutableDictionary<int, int>.Empty.Add(0, 0),
                },
            };
        }
        var instruction = CreateInstructionRequest(
            CilOperation.Call,
            [kind, kind],
            context: context,
            maxStack: 2);
        var request = new RuntimeIntrinsicEmissionRequest(
            new CallEmissionRequest(instruction, method, 0, 2),
            RuntimeIntrinsic.IntegerDivRem,
            null,
            targetLayout,
            CreateFunctionIndexResolver());
        var layouts = new RecordingLayoutProvider(targetLayout);
        var code = GetCodeWriter(instruction);
        var exceptions = new RecordingImplicitExceptionEmitter();

        new IntegerDivRemIntrinsicEmitter(
            layouts,
            new DivRemLayouts(tuple, element, size * 2, size),
            new ValueFrameAddressEmitter(layouts),
            exceptions).Emit(request, code);

        var instructions = ((RecordingInstructionWriter)code).ToInstructions();
        Assert.Equal(promoted, instructions.Any(instruction =>
            instruction.Opcode == WasmOpcodes.LocalSet &&
            instruction.Operand.UnsignedValue == 40U));
        Assert.Equal(promoted, instructions.Any(instruction =>
            instruction.Opcode == WasmOpcodes.LocalSet &&
            instruction.Operand.UnsignedValue == 41U));
        var unsigned = primitive is "u1" or "u2" or "char" or "u4" or "u8" or "nativeuint";
        var expectedDivide = (size == 8, unsigned) switch
        {
            (true, true) => WasmOpcodes.I64DivideUnsigned,
            (true, false) => WasmOpcodes.I64DivideSigned,
            (false, true) => WasmOpcodes.I32DivideUnsigned,
            _ => WasmOpcodes.I32DivideSigned,
        };
        Assert.Single(instructions.Where(instruction => instruction.Opcode == expectedDivide));
        Assert.Equal(promoted ? 0 : 2, instructions.Count(instruction =>
            instruction.Opcode is >= WasmOpcodes.I32Store and <= WasmOpcodes.I32Store16));
        Assert.Equal(primitive is "i4" or "i8" or "nativeint"
                ? [ManagedExceptionKind.DivideByZero, ManagedExceptionKind.Overflow]
                : new[] { ManagedExceptionKind.DivideByZero },
            exceptions.Kinds);
    }

    private sealed class DivRemLayouts(
        CliTypeIdentity tuple,
        CliTypeIdentity element,
        int resultSize = 16,
        int elementSize = 8) : IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            type.Equals(tuple)
                ? new(type, resultSize, elementSize, [])
                : type.Equals(element)
                    ? new(type, elementSize, elementSize, [])
                    : throw new InvalidOperationException(type.CanonicalName);
    }

    private sealed class RecordingImplicitExceptionEmitter : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind, OptionalFunctionIndex throwFunctionIndex)
        {
            Kinds.Add(kind);
        }
    }
}
