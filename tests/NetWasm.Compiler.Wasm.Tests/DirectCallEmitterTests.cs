using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class DirectCallEmitterTests
{
    [Fact]
    public void StaticCallConsumesArgumentsAndProducesResult()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var instruction = CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.I4, CliValueKind.I4],
            new CilOperand.Entity(EntryKey));
        var definition = program.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliValueKind.I4,
                CliValueKind.I4,
                CliValueKind.I4),
        };
        var method = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            [],
            definition.Signature);
        var emitter = CreateEmitter(layouts);

        emitter.Emit(
            new CallEmissionRequest(instruction, method, 0, 2),
            GetCodeWriter(instruction),
            CreateIndices(program));

        Assert.Equal([CliValueKind.I4], instruction.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(instruction));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32EqualZero)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64EqualZero)]
    public void VirtualReferenceCallChecksNullForEachMemoryWidth(
        WasmTarget target,
        byte expectedEquality)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var instruction = CreateInstructionRequest(
            CilOperation.CallVirtual,
            [CliValueKind.ManagedReference],
            new CilOperand.Entity(ConstructorKey));
        var definition = program.GetMethod(ConstructorKey) with
        {
            Name = "Virtual",
            IsVirtual = true,
        };
        var method = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            [],
            definition.Signature);

        CreateEmitter(layouts).Emit(
            new CallEmissionRequest(instruction, method, 0, 1),
            GetCodeWriter(instruction),
            CreateIndices(program));

        Assert.Empty(instruction.Stack);
        Assert.Contains(expectedEquality, GetCodeBytes(instruction));
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(instruction));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 0, WasmOpcodes.I32Constant)]
    [InlineData(WasmTarget.Wasm64, 8, WasmOpcodes.I64Constant)]
    public void ValueTypeReturnUsesValueFrameForEachMemoryWidth(
        WasmTarget target,
        int returnOffset,
        byte expectedConstant)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true);
        var signature = MethodSignatureModel.Create(valueType);
        var definition = new MethodDefinitionModel(
            EntryKey,
            TypeKey,
            "GetValue",
            true,
            signature,
            1);
        var method = new MethodInstanceModel(
            definition,
            valueType,
            [],
            signature);
        var instruction = CreateInstructionRequest(
            CilOperation.Call,
            [],
            context: CreateMethodEmissionContext() with
            {
                ValueLayout = new ValueFrameLayout(
                    16,
                    [],
                    [],
                    ImmutableDictionary<int, int>.Empty.Add(0, returnOffset),
                    []),
            });

        CreateEmitter(layouts).Emit(
            new CallEmissionRequest(instruction, method, 0, 0),
            GetCodeWriter(instruction),
            CreateIndices(program));

        Assert.Equal([CliValueKind.ValueType], instruction.Stack);
        var code = GetCodeBytes(instruction);
        if (returnOffset == 0)
        {
            Assert.DoesNotContain(WasmOpcodes.I32Constant, code);
        }
        else
        {
            Assert.Contains(expectedConstant, code);
        }
        Assert.Contains(WasmOpcodes.Call, code);
    }

    [Fact]
    public void ValueTypeAddressReceiverIsPassedWithoutCopying()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var instruction = CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.ManagedAddress],
            new CilOperand.Entity(ConstructorKey));
        var definition = program.GetMethod(ConstructorKey);
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true,
            CliValueKind.ValueType);
        var method = new MethodInstanceModel(
            definition,
            valueType,
            [],
            definition.Signature);

        CreateEmitter(layouts).Emit(
            new CallEmissionRequest(instruction, method, 0, 1),
            GetCodeWriter(instruction),
            CreateIndices(program));

        Assert.Empty(instruction.Stack);
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(instruction));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ValueFrameReceiverIsPassedWithoutCopying(WasmTarget target)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var instruction = CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.ValueType],
            new CilOperand.Entity(ConstructorKey));
        var definition = program.GetMethod(ConstructorKey);
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true,
            CliValueKind.ValueType);
        var method = new MethodInstanceModel(
            definition,
            valueType,
            [],
            definition.Signature);

        CreateEmitter(layouts).Emit(
            new CallEmissionRequest(instruction, method, 0, 1),
            GetCodeWriter(instruction),
            CreateIndices(program));

        Assert.Empty(instruction.Stack);
        Assert.DoesNotContain(WasmOpcodes.I32Store, GetCodeBytes(instruction));
        Assert.DoesNotContain(WasmOpcodes.I64Store, GetCodeBytes(instruction));
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(instruction));
    }

    [Fact]
    public void ClosedReferenceConstrainedReceiverLoadsTheReferenceFromItsAddressSlot()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var instruction = CreateInstructionRequest(
            CilOperation.CallVirtual,
            [CliValueKind.ManagedAddress],
            new CilOperand.Entity(ConstructorKey));
        var method = CreateMethodOperands(program).Resolve(
            instruction);
        var emitter = CreateEmitter(layouts);

        emitter.Emit(new CallEmissionRequest(
            instruction,
            method.Target,
            0,
            1,
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false)),
            GetCodeWriter(instruction),
            CreateIndices(program));

        Assert.Empty(instruction.Stack);
        Assert.Contains(WasmOpcodes.I32Load, GetCodeBytes(instruction));
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(instruction));
    }

    [Fact]
    public void Memory64ConstrainedReferenceReceiverReadsTheActualNativePointerLocal()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var context = CreateMethodEmissionContext();
        var instruction = CreateInstructionRequest(
            CilOperation.CallVirtual,
            [CliValueKind.NativeInt],
            new CilOperand.Entity(ConstructorKey),
            context);
        var method = CreateMethodOperands(program).Resolve(instruction);

        CreateEmitter(layouts).Emit(
            new CallEmissionRequest(
                instruction,
                method.Target,
                0,
                1,
                CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false)),
            GetCodeWriter(instruction),
            CreateIndices(program));

        var nativeLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            context.StackLocals,
            0,
            CliValueKind.NativeInt,
            WasmTargetLayout.Wasm64);
        var instructions = ((RecordingInstructionWriter)GetCodeWriter(instruction)).ToInstructions();
        Assert.Equal(WasmOpcodes.LocalGet, instructions[0].Opcode);
        Assert.Equal((uint)nativeLocal, instructions[0].Operand.UnsignedValue);
    }

    [Fact]
    public void Memory64PointerToScalarValueReceiverRemainsAnAddress()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var context = CreateMethodEmissionContext();
        var instruction = CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.NativeInt],
            new CilOperand.Entity(ConstructorKey),
            context);
        var definition = program.GetMethod(ConstructorKey);
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true,
            CliValueKind.I4);
        var method = new MethodInstanceModel(
            definition,
            valueType,
            [],
            definition.Signature);

        CreateEmitter(layouts).Emit(
            new CallEmissionRequest(instruction, method, 0, 1),
            GetCodeWriter(instruction),
            CreateIndices(program));

        var nativeLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            context.StackLocals,
            0,
            CliValueKind.NativeInt,
            WasmTargetLayout.Wasm64);
        var instructions = ((RecordingInstructionWriter)GetCodeWriter(instruction)).ToInstructions();
        Assert.DoesNotContain(instructions, item => item.Opcode == WasmOpcodes.I32Store);
        Assert.Equal(WasmOpcodes.LocalGet, instructions[0].Opcode);
        Assert.Equal((uint)nativeLocal, instructions[0].Operand.UnsignedValue);
    }

    [Fact]
    public void ScalarValueReceiverIsCopiedIntoTheValueFrame()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 4),
                []),
        };
        var instruction = CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.I4],
            new CilOperand.Entity(ConstructorKey),
            context);
        var definition = program.GetMethod(ConstructorKey);
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true,
            CliValueKind.I4);
        var method = new MethodInstanceModel(
            definition,
            valueType,
            [],
            definition.Signature);

        CreateEmitter(layouts).Emit(
            new CallEmissionRequest(instruction, method, 0, 1),
            GetCodeWriter(instruction),
            CreateIndices(program));

        Assert.Empty(instruction.Stack);
        Assert.Contains(WasmOpcodes.I32Store, GetCodeBytes(instruction));
        Assert.Contains(WasmOpcodes.Call, GetCodeBytes(instruction));
    }

    private static ICallEmitter CreateEmitter(RecordingLayoutProvider layouts) =>
        new[]
        {
            new DirectCallEmitter(
                layouts,
                layouts,
                new ImplicitExceptionEmitter(layouts, layouts, 7)),
        }.Cast<ICallEmitter>().Single();

    private static FunctionIndexResolver CreateIndices(FakeProgram program) => new(
        program,
        program,
        new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                EntryKey,
                new WasmFunctionIndex(30)).Add(
                ConstructorKey,
                new WasmFunctionIndex(31)),
            [],
            [],
            []));
}
