using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static DelegateEmitterTestSupport;
using static EmitterTestSupport;

public sealed class DelegateInvokeFunctionEmitterTests
{
    private const int MulticastFunction = 61;

    // A delegate without an invocation list dispatches straight to its target and returns,
    // rooting nothing; one with a list passes every parameter to the type's multicast
    // function and returns what that returns.
    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I8, false)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I8, false)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.Void, false)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.Void, true)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.Void, true)]
    public void DispatchesASingleCastDelegateAndHandsAListToTheMulticastFunction(
        WasmTarget target, CliValueKind result, bool valueResult)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var callbackType = CallbackType();
        var arguments = new[] { CliTypeIdentity.FromStackKind(CliValueKind.I4), CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference) };
        var invoke = valueResult
            ? CreateInvoke(callbackType, CliTypeIdentity.Named(Assembly, "Test", "Result", isValueType: true), arguments)
            : CreateInvoke(callbackType, CliTypeIdentity.FromStackKind(result), arguments);
        var method = CreateTarget(invoke.Signature, "Target", isStatic: true, Key(0x06000025));
        var program = CreateTargetProgram(invoke, method);
        var resolver = CreateResolver(callbackType, method);
        var writers = new RecordingWriterFactory();
        var dispatch = new RecordingDispatch();
        var parameters = valueResult ? 4 : 3;
        var receiver = valueResult ? 1 : 0;

        var function = Emit(CreateEmitter(layouts, writers, dispatch), invoke, program, resolver);

        var wide = layouts.Target.UsesMemory64;
        var expected = new List<WasmInstruction>
        {
            LocalGet(receiver),
            WasmInstruction.WithOperand(wide ? WasmOpcodes.I64Load : WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(wide ? 3u : 2u, (uint)layouts.DelegateInvocationListOffset)),
            WasmInstruction.NoOperand(wide ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero),
            WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)),
            DispatchMarker,
        };
        if (result == CliValueKind.I8)
        {
            expected.Add(LocalGet(parameters));
        }
        expected.Add(WasmInstruction.NoOperand(WasmOpcodes.Return));
        expected.Add(WasmInstruction.NoOperand(WasmOpcodes.End));
        expected.AddRange(Enumerable.Range(0, parameters).Select(LocalGet));
        expected.Add(Call(MulticastFunction));
        expected.Add(WasmInstruction.NoOperand(WasmOpcodes.End));
        Assert.Equal(expected, writers.Instructions.ToInstructions());
        var request = Assert.Single(dispatch.Requests);
        Assert.Equal(new DelegateDispatch(invoke, program, receiver, parameters, parameters + 1, parameters + 2), request.Dispatch);
        Assert.Same(resolver, request.FunctionIndices);
        Assert.Equal(LocalDeclarations(target, result == CliValueKind.I8 ? CliValueKind.I8 : CliValueKind.I4,
            CliValueKind.ManagedAddress), function);
    }

    // The method id local is declared only when the dispatch reads the id once, past the
    // targets it tests in turn.
    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void DeclaresTheMethodIdLocalOnlyPastTheSequentialTargets(int count, bool declared)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(WasmTarget.Wasm32));
        var callbackType = CallbackType();
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        var invoke = CreateInvoke(callbackType, signature);
        var methods = Enumerable.Range(0, count)
            .Select(index => CreateTarget(signature, $"Target{index}", true, Key(0x06000030 + index)))
            .ToArray();
        var writers = new RecordingWriterFactory();

        var function = Emit(CreateEmitter(layouts, writers, new RecordingDispatch()), invoke,
            CreateTargetProgram(invoke, methods), CreateResolver(callbackType, methods));

        CliValueKind[] locals = declared
            ? [CliValueKind.I4, CliValueKind.ManagedAddress, CliValueKind.I4]
            : [CliValueKind.I4, CliValueKind.ManagedAddress];
        Assert.Equal(LocalDeclarations(WasmTarget.Wasm32, locals), function);
    }

    private static byte[] Emit(DelegateInvokeFunctionEmitter emitter, MethodInstanceModel invoke,
        DelegateInvokeTarget program, IFunctionIndexResolver resolver) =>
        ((IDelegateInvokeFunctionEmitter)emitter).Emit(invoke, program, resolver, new WasmFunctionIndex(MulticastFunction));

    private static DelegateInvokeFunctionEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        IGeneratedFunctionWriterFactory writers,
        IDelegateDispatchEmitter dispatch) =>
        new(
            layouts,
            layouts,
            new ManagedMethodFunctionTypeResolver(),
            new DelegateInvocationTargetSelector(),
            writers,
            dispatch);
}
