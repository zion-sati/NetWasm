using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ConstantsStackEmitterTests
{
    [Theory]
    [InlineData(CilOperation.Nop)]
    [InlineData(CilOperation.Break)]
    public void NoOperationCommandsLeaveTheStackUnchanged(CilOperation operation)
    {
        var request = CreateRequest(operation);

        CreateEmitter().Emit(request, GetCodeWriter(request));

        Assert.Empty(request.Stack);
    }

    [Theory]
    [InlineData(CilOperation.LoadInt32, CliValueKind.I4)]
    [InlineData(CilOperation.LoadInt64, CliValueKind.I8)]
    [InlineData(CilOperation.LoadFloat32, CliValueKind.F4)]
    [InlineData(CilOperation.LoadFloat64, CliValueKind.F8)]
    [InlineData(CilOperation.LoadNull, CliValueKind.ManagedReference)]
    public void ScalarConstantUsesExpectedStackKind(
        CilOperation operation,
        CliValueKind expected)
    {
        var request = CreateRequest(operation);

        CreateEmitter().Emit(request, GetCodeWriter(request));

        Assert.Equal([expected], request.Stack);
    }

    [Fact]
    public void StringAndTypeTokensUseLayoutAddressesAndIds()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = CreateEmitter(layouts);
        var stringRequest = CreateRequest(
            CilOperation.LoadString,
            new CilOperand.UserString("hello"));
        var typeRequest = CreateRequest(
            CilOperation.LoadTypeToken,
            new CilOperand.Entity(TypeKey));

        emitter.Emit(stringRequest, GetCodeWriter(stringRequest));
        emitter.Emit(typeRequest, GetCodeWriter(typeRequest));

        Assert.Equal("hello", layouts.StringRequest);
        Assert.Equal([CliValueKind.ManagedReference], stringRequest.Stack);
        Assert.Equal([CliValueKind.I4], typeRequest.Stack);
    }

    [Fact]
    public void DuplicateAndPopUpdateEvaluationStackWithoutAliasingState()
    {
        var emitter = CreateEmitter();
        var duplicate = CreateRequest(CilOperation.Duplicate);
        duplicate.Stack.Add(CliValueKind.I4);

        emitter.Emit(duplicate, GetCodeWriter(duplicate));
        Assert.Equal([CliValueKind.I4, CliValueKind.I4], duplicate.Stack);

        var pop = duplicate with
        {
            Instruction = I(1, CilOperation.Pop),
        };
        emitter.Emit(pop, GetCodeWriter(duplicate));
        Assert.Equal([CliValueKind.I4], pop.Stack);
    }

    [Fact]
    public void Memory64NullUsesI64Constant()
    {
        var request = CreateRequest(CilOperation.LoadNull);

        CreateEmitter(new RecordingLayoutProvider(WasmTargetLayout.Wasm64)).Emit(
            request,
            GetCodeWriter(request));

        Assert.Equal(WasmOpcodes.I64Constant, GetCodeBytes(request)[0]);
    }

    [Theory]
    [InlineData(false, WasmOpcodes.I32Constant)]
    [InlineData(true, WasmOpcodes.I64Constant)]
    public void MemberTokensUseNativeWidthDescriptorAddresses(
        bool memory64,
        byte expectedOpcode)
    {
        var layouts = new RecordingLayoutProvider(
            memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32);
        var program = new FakeProgram();
        var field = program.GetField(InstanceFieldKey);
        var fieldInstance = new FieldInstanceModel(
            field,
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            field.SignatureType);
        var fieldRequest = CreateRequest(
            CilOperation.LoadFieldToken,
            new CilOperand.FieldInstance(fieldInstance));
        var method = program.GetMethod(EntryKey);
        var methodInstance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            [],
            method.Signature);
        var methodRequest = CreateRequest(
            CilOperation.LoadMethodToken,
            new CilOperand.MethodInstance(methodInstance));

        var emitter = CreateEmitter(layouts);
        emitter.Emit(fieldRequest, GetCodeWriter(fieldRequest));
        emitter.Emit(methodRequest, GetCodeWriter(methodRequest));

        Assert.Equal(CliValueKind.NativeInt, Assert.Single(fieldRequest.Stack));
        Assert.Equal(CliValueKind.NativeInt, Assert.Single(methodRequest.Stack));
        Assert.Equal(expectedOpcode, GetCodeBytes(fieldRequest)[0]);
        Assert.Equal(expectedOpcode, GetCodeBytes(methodRequest)[0]);
        Assert.Same(fieldInstance, layouts.FieldDescriptorRequest);
        Assert.Same(methodInstance, layouts.MethodDescriptorRequest);
    }

    private static ConstantsStackEmitter CreateEmitter(
        RecordingLayoutProvider? layouts = null)
    {
        var actualLayouts = layouts ?? new RecordingLayoutProvider();
        return new ConstantsStackEmitter(
            actualLayouts,
            actualLayouts,
            actualLayouts,
            actualLayouts,
            CreateTypeOperands(new FakeProgram()));
    }

    private static InstructionEmissionRequest CreateRequest(
        CilOperation operation,
        CilOperand? operand = null)
    {
        var actualOperand = operand ?? operation switch
        {
            CilOperation.LoadInt32 => new CilOperand.ConstantI4(7),
            CilOperation.LoadInt64 => new CilOperand.ConstantI8(7),
            CilOperation.LoadFloat32 => new CilOperand.ConstantF4(7),
            CilOperation.LoadFloat64 => new CilOperand.ConstantF8(7),
            _ => new CilOperand.None(),
        };
        return CreateInstructionRequest(operation, operand: actualOperand);
    }
}
