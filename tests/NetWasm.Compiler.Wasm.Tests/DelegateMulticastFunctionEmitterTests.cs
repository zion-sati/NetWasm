using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static DelegateEmitterTestSupport;
using static EmitterTestSupport;

public sealed class DelegateMulticastFunctionEmitterTests
{
    private const int InvokeFunction = 43;

    // The function roots the delegate and its arguments, dispatches the list's first entry,
    // goes round again only while the next index is below the count, reading that entry
    // there, then leaves the root frame and returns the last entry's result.
    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I8, false, false)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I8, false, true)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.Void, false, true)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.Void, true, false)]
    public void RootsThenDispatchesEachEntryOfTheList(
        WasmTarget target, CliValueKind result, bool valueResult, bool inlineArrayData)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var callbackType = CallbackType();
        var argument = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);
        var invoke = valueResult
            ? CreateInvoke(callbackType, CliTypeIdentity.Named(Assembly, "Test", "Result", isValueType: true), argument)
            : CreateInvoke(callbackType, CliTypeIdentity.FromStackKind(result), argument);
        var method = CreateTarget(invoke.Signature, "Target", isStatic: true, Key(0x06000025));
        var program = CreateTargetProgram(invoke, method) with
        {
            InlineArrayData = inlineArrayData,
            InlineMulticastDispatch = true,
        };
        var resolver = CreateResolver(callbackType, method);
        var writers = new RecordingWriterFactory();
        var dispatch = new RecordingDispatch();
        var roots = new RecordingRoots();
        var parameters = valueResult ? 3 : 2;
        var receiver = valueResult ? 1 : 0;
        var (resultLocal, frame, list, count, index, current) =
            (parameters, parameters + 1, parameters + 2, parameters + 3, parameters + 4, parameters + 5);

        var function = ((IDelegateMulticastFunctionEmitter)CreateEmitter(layouts, writers, roots, dispatch))
            .Emit(invoke, program, resolver, new(InvokeFunction));

        var wide = layouts.Target.UsesMemory64;
        var referenceLoad = wide ? WasmOpcodes.I64Load : WasmOpcodes.I32Load;
        var alignment = wide ? 3u : 2u;
        var entry = new RecordingInstructionWriter();
        ManagedMemoryEmitter.EmitArrayElementAddress(entry, layouts.Target, list, index, inlineArrayData,
            layouts.Target.ObjectReferenceSize);
        entry.Write(WasmInstruction.WithOperand(referenceLoad, WasmInstructionOperand.Memory(alignment, 0)));
        entry.Write(LocalSet(current));
        var expected = new List<WasmInstruction>
        {
            WasmInstruction.NoOperand(WasmOpcodes.Drop),
            LocalGet(receiver),
            WasmInstruction.WithOperand(referenceLoad,
                WasmInstructionOperand.Memory(alignment, (uint)layouts.DelegateInvocationListOffset)),
            LocalSet(list),
            LocalGet(receiver),
            WasmInstruction.WithOperand(WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(2, (uint)layouts.DelegateInvocationCountOffset)),
            LocalSet(count),
            WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)),
            LocalSet(index),
        };
        expected.AddRange(entry.ToInstructions());
        expected.AddRange(
        [
            WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)),
            WasmInstruction.WithOperand(WasmOpcodes.Loop, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)),
            DispatchMarker,
            LocalGet(index),
            WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(1)),
            WasmInstruction.NoOperand(WasmOpcodes.I32Add),
            WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned((uint)index)),
            LocalGet(count),
            WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned),
            WasmInstruction.WithOperand(WasmOpcodes.BranchIf, WasmInstructionOperand.Unsigned(1)),
        ]);
        expected.AddRange(entry.ToInstructions());
        expected.AddRange(
        [
            WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(0)),
            WasmInstruction.NoOperand(WasmOpcodes.End),
            WasmInstruction.NoOperand(WasmOpcodes.End),
            LocalGet(frame),
            Call(RuntimeImport(RuntimeImportSymbol.RootFrameLeave)),
        ]);
        if (result == CliValueKind.I8)
        {
            expected.Add(LocalGet(resultLocal));
        }
        expected.Add(WasmInstruction.NoOperand(WasmOpcodes.End));
        Assert.Equal(expected, writers.Instructions.ToInstructions());
        Assert.Equal((invoke, frame), Assert.Single(roots.Requests));
        var request = Assert.Single(dispatch.Requests);
        Assert.Equal(new DelegateDispatch(invoke, program, current, resultLocal, parameters + 6, parameters + 7), request.Dispatch);
        Assert.Same(resolver, request.FunctionIndices);
        Assert.Equal(LocalDeclarations(target, result == CliValueKind.I8 ? CliValueKind.I8 : CliValueKind.I4,
            CliValueKind.ManagedAddress, CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4,
            CliValueKind.ManagedReference, CliValueKind.ManagedAddress), function);
    }

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

        var function = ((IDelegateMulticastFunctionEmitter)CreateEmitter(
                layouts, writers, new RecordingRoots(), new RecordingDispatch()))
            .Emit(invoke, CreateTargetProgram(invoke, methods) with { InlineMulticastDispatch = true },
                CreateResolver(callbackType, methods), new(InvokeFunction));

        List<CliValueKind> locals =
        [
            CliValueKind.I4, CliValueKind.ManagedAddress, CliValueKind.ManagedReference, CliValueKind.I4,
            CliValueKind.I4, CliValueKind.ManagedReference, CliValueKind.ManagedAddress,
        ];
        if (declared)
        {
            locals.Add(CliValueKind.I4);
        }
        Assert.Equal(LocalDeclarations(WasmTarget.Wasm32, [.. locals]), function);
    }

    // Without its own dispatch, the function hands each entry to the invoke function in the
    // delegate's place, with the same arguments, and keeps a scalar result; it needs neither
    // the initialization frame nor the method id.
    [Theory]
    [InlineData(CliValueKind.I8, false)]
    [InlineData(CliValueKind.Void, false)]
    [InlineData(CliValueKind.Void, true)]
    public void HandsEachEntryToTheInvokeFunctionWithoutItsOwnDispatch(CliValueKind result, bool valueResult)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(WasmTarget.Wasm32));
        var callbackType = CallbackType();
        var argument = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);
        var invoke = valueResult
            ? CreateInvoke(callbackType, CliTypeIdentity.Named(Assembly, "Test", "Result", isValueType: true), argument)
            : CreateInvoke(callbackType, CliTypeIdentity.FromStackKind(result), argument);
        var methods = Enumerable.Range(0, 5)
            .Select(index => CreateTarget(invoke.Signature, $"Target{index}", true, Key(0x06000030 + index)))
            .ToArray();
        var writers = new RecordingWriterFactory();
        var dispatch = new RecordingDispatch();
        var parameters = valueResult ? 3 : 2;
        var current = parameters + 5;

        var function = ((IDelegateMulticastFunctionEmitter)CreateEmitter(layouts, writers, new RecordingRoots(), dispatch))
            .Emit(invoke, CreateTargetProgram(invoke, methods), CreateResolver(callbackType, methods), new(InvokeFunction));

        Assert.Empty(dispatch.Requests);
        var instructions = writers.Instructions.ToInstructions();
        var call = Assert.Single(Enumerable.Range(0, instructions.Length), index => instructions[index] == Call(InvokeFunction));
        var arguments = valueResult
            ? new[] { LocalGet(0), LocalGet(current), LocalGet(2) }
            : [LocalGet(current), LocalGet(1)];
        Assert.Equal(arguments, instructions[(call - arguments.Length)..call]);
        Assert.Equal(result == CliValueKind.I8, instructions[call + 1] == LocalSet(parameters));
        Assert.Equal(LocalDeclarations(WasmTarget.Wasm32, result == CliValueKind.I8 ? CliValueKind.I8 : CliValueKind.I4,
            CliValueKind.ManagedAddress, CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4,
            CliValueKind.ManagedReference), function);
    }

    private static DelegateMulticastFunctionEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        IGeneratedFunctionWriterFactory writers,
        IDelegateArgumentRootEmitter roots,
        IDelegateDispatchEmitter dispatch) =>
        new(
            layouts,
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            new ManagedMethodFunctionTypeResolver(),
            new DelegateInvocationTargetSelector(),
            writers,
            roots,
            dispatch);
}
