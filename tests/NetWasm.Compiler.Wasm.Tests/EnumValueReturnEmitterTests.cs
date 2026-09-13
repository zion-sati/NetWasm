using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class EnumValueReturnEmitterTests
{
    [Fact]
    public void TypeCodeIntrinsicDelegatesValueTypePublication()
    {
        var operation = new RecordingTypeCodeEmitter();
        var valueReturn = new RecordingValueReturnEmitter();
        var request = CreateRequest(WasmTargetLayout.Wasm64);

        new EnumTypeCodeIntrinsicEmitter(operation, valueReturn).Emit(
            request,
            EmitterTestSupport.GetCodeWriter(request.Instruction));

        Assert.True(operation.Called);
        Assert.True(valueReturn.Called);
        Assert.Equal(request.Instruction.Context.NumericTemporaryI4, valueReturn.ValueLocal);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Constant, WasmOpcodes.I32Add)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Constant, WasmOpcodes.I64Add)]
    public void PublishesScalarEnumValueThroughTheValueFrameForEachTarget(
        WasmTarget target,
        byte addressConstant,
        byte addressAdd)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = Assert.IsAssignableFrom<IEnumValueReturnEmitter>(
            new EnumValueReturnEmitter(
                new ValueFrameAddressEmitter(layouts),
                layouts,
                layouts));
        var request = CreateRequest(layouts.Target);
        var code = EmitterTestSupport.GetCodeWriter(request.Instruction);

        emitter.Emit(code, request, request.Instruction.Context.NumericTemporaryI4);

        var bytes = EmitterTestSupport.GetCodeBytes(request.Instruction);
        Assert.Contains(addressConstant, bytes);
        Assert.Contains(addressAdd, bytes);
        Assert.Contains(WasmOpcodes.I32Store, bytes);
        Assert.Contains(WasmOpcodes.LocalSet, bytes);
    }

    private static RuntimeIntrinsicEmissionRequest CreateRequest(
        WasmTargetLayout target)
    {
        var assembly = new AssemblyIdentity("EnumValueReturnTests");
        var declaringType = new EntityKey(assembly, 0x02000001);
        var methodKey = new EntityKey(assembly, 0x06000001);
        var returnType = CliTypeIdentity.Named(
            assembly,
            "System",
            "TypeCode",
            isValueType: true,
            CliValueKind.ValueType);
        var signature = new MethodSignatureModel(returnType, []);
        var definition = new MethodDefinitionModel(
            methodKey,
            declaringType,
            "InternalGetTypeCode",
            true,
            signature,
            0);
        var instance = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(assembly, "System", "Enum", isValueType: false),
            [],
            signature);
        var context = EmitterTestSupport.CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(
                8,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 4),
                [])
        };
        var instruction = EmitterTestSupport.CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.ManagedReference],
            context: context);
        return new(
            new CallEmissionRequest(instruction, instance, 0, 1),
            RuntimeIntrinsic.EnumGetTypeCode,
            null,
            target,
            EmitterTestSupport.CreateFunctionIndexResolver(new FakeProgram()));
    }

    private sealed class RecordingTypeCodeEmitter : IEnumTypeCodeEmitter
    {
        public bool Called { get; private set; }

        public void EmitTypeCode(
            IWasmInstructionWriter code,
            int receiver,
            int result,
            int temporaryI4,
            CliTypeIdentity? constrainedType = null,
            CliValueKind receiverKind = CliValueKind.ManagedReference) => Called = true;
    }

    private sealed class RecordingValueReturnEmitter : IEnumValueReturnEmitter
    {
        public bool Called { get; private set; }
        public int ValueLocal { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            RuntimeIntrinsicEmissionRequest request,
            int valueLocal)
        {
            Called = true;
            ValueLocal = valueLocal;
        }
    }
}
