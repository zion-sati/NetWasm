using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class JSImportInvocationEmitterTests
{
    private const int RawImportIndex = 40;
    private const int ForwarderIndex = 50;

    [Theory]
    [InlineData(false, 2, 0, 0)]
    [InlineData(true, 1, 1, 1)]
    public void EmitsArgumentsCallbackFailureCleanupAndResultThroughFocusedActions(
        bool hasCallback,
        int expectedArgumentCount,
        int expectedCallbackCount,
        int expectedReleaseCount)
    {
        var program = new FakeProgram();
        var method = CreateImport(program);
        var target = CreateInstructionModuleTarget(program);
        if (hasCallback)
        {
            var callbackMethod = CreateMethodInstance(program.GetMethod(EntryKey));
            target = target with
            {
                HostCallbacks = ImmutableDictionary<(EntityKey, int), HostCallbackDeclaration>
                    .Empty.Add(
                        (EntryKey, 0),
                        new HostCallbackDeclaration(EntryKey, 0, callbackMethod, "callback")),
            };
        }
        var arguments = new RecordingArgumentEmitter();
        var callbackHandles = new RecordingCallbackHandleEmitter();
        var handles = new RecordingHandleReleaser();
        var results = new RecordingResultEmitter();
        var exceptions = new RecordingExceptionEmitter();
        var emitter = Assert.IsAssignableFrom<IJSImportInvocationEmitter>(new JSImportInvocationEmitter(
            new RecordingLayoutProvider(),
            WasmRuntimeImports.CreateCatalog(),
            new FixedDescriptorSizeResolver(16),
            arguments,
            callbackHandles,
            handles,
            results,
            exceptions,
            new FixedStackLocalResolver(9)));
        var code = new RecordingInstructionWriter();

        emitter.Emit(
            new JSImportInvocation(
                method,
                CreateMethodEmissionContext(),
                [CliValueKind.ManagedReference, CliValueKind.I4],
                0,
                2,
                target,
                0),
            code,
            CreateResolver(program));

        Assert.Equal(expectedArgumentCount, arguments.Count);
        Assert.Equal(expectedCallbackCount, callbackHandles.Count);
        Assert.Equal(expectedReleaseCount, handles.Count);
        Assert.Equal([ManagedExceptionKind.JSException], exceptions.Kinds);
        Assert.NotNull(results.Request);
        Assert.Equal(hasCallback, results.Request.Callback is not null);
    }

    [Fact]
    public void CallsTheRawInteropImportEvenWhenAManagedForwarderExists()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var emitter = Assert.IsAssignableFrom<IJSImportInvocationEmitter>(new JSImportInvocationEmitter(
            layouts,
            runtimeImports,
            new FixedDescriptorSizeResolver(16),
            new RecordingArgumentEmitter(),
            new RecordingCallbackHandleEmitter(),
            new RecordingHandleReleaser(),
            new RecordingResultEmitter(),
            new RecordingExceptionEmitter(),
            new FixedStackLocalResolver(9)));
        var code = new RecordingInstructionWriter();
        var functionIndices = CreateResolver(program);

        emitter.Emit(
            new JSImportInvocation(
                CreateImport(program),
                CreateMethodEmissionContext(),
                [CliValueKind.ManagedReference, CliValueKind.I4],
                0,
                2,
                CreateInstructionModuleTarget(program), 0),
            code,
            functionIndices);

        var calls = code.ToInstructions()
            .Where(instruction => instruction.Opcode == WasmOpcodes.Call)
            .Select(instruction => instruction.Operand.UnsignedValue)
            .ToArray();
        Assert.Contains((uint)RawImportIndex, calls);
        Assert.DoesNotContain((uint)ForwarderIndex, calls);
        Assert.Equal(ForwarderIndex, functionIndices.Resolve(EntryKey));
    }

    // A scalar import passes its arguments and takes its result as Wasm
    // values: no descriptor, no status and no result marshalling. A host
    // throw reaches the catch_all and raises JSException.
    [Fact]
    public void CallsAScalarImportDirectlyAndRaisesJSExceptionWhenTheHostThrows()
    {
        var program = new FakeProgram();
        var method = CreateMethodInstance(program.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4, CliValueKind.I4),
            JSImport = new("add", "tests"),
        });
        var arguments = new RecordingArgumentEmitter();
        var results = new RecordingResultEmitter();
        var exceptions = new RecordingExceptionEmitter();
        var emitter = Assert.IsAssignableFrom<IJSImportInvocationEmitter>(new JSImportInvocationEmitter(
            new RecordingLayoutProvider(),
            WasmRuntimeImports.CreateCatalog(),
            new FixedDescriptorSizeResolver(16),
            arguments,
            new RecordingCallbackHandleEmitter(),
            new RecordingHandleReleaser(),
            results,
            exceptions,
            new FixedStackLocalResolver(9)));
        var code = new RecordingInstructionWriter();
        List<CliValueKind> stack = [CliValueKind.I4, CliValueKind.I4];

        emitter.Emit(
            new JSImportInvocation(method, CreateMethodEmissionContext(), stack, 0, 2,
                CreateInstructionModuleTarget(program), 0),
            code,
            CreateResolver(program));

        var instructions = code.ToInstructions();
        Assert.Equal(2, arguments.Count);
        Assert.Null(results.Request);
        Assert.Equal([ManagedExceptionKind.JSException], exceptions.Kinds);
        Assert.Equal(
            [WasmOpcodes.Block, WasmOpcodes.Block, WasmOpcodes.TryTable, WasmOpcodes.Call, WasmOpcodes.End,
                WasmOpcodes.Branch, WasmOpcodes.End, WasmOpcodes.Unreachable, WasmOpcodes.End, WasmOpcodes.LocalSet],
            instructions.Select(instruction => instruction.Opcode));
        Assert.Equal((uint)RawImportIndex, instructions[3].Operand.UnsignedValue);
        Assert.Equal(9u, instructions[^1].Operand.UnsignedValue);
        Assert.Equal([CliValueKind.I4], stack);
    }

    private static MethodInstanceModel CreateImport(FakeProgram program) =>
        CreateMethodInstance(program.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4),
            JSImport = new("run", "tests"),
        });

    private static IFunctionIndexResolver CreateResolver(FakeProgram program) =>
        CreateFunctionIndexResolver(
            program,
            new FunctionIndexMap([], [], [],
                ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(EntryKey, new(RawImportIndex)))
            {
                JSImportForwarders = ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty
                    .Add(EntryKey, new(ForwarderIndex)),
            });

    private static MethodInstanceModel CreateMethodInstance(MethodDefinitionModel method) => new(
        method,
        CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
        [],
        method.Signature);

    private sealed class FixedStackLocalResolver(int local) : IStackLocalResolver
    {
        public int Resolve(MethodEmissionContext context, int slot, CliValueKind type) => local;
    }

    private sealed class FixedDescriptorSizeResolver(int size) :
        IJavaScriptResultDescriptorSizeResolver
    {
        public int Resolve(CliTypeIdentity result) => size;
    }

    private sealed class RecordingArgumentEmitter : IJavaScriptImportArgumentEmitter
    {
        public int Count { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            CliTypeIdentity type,
            int local,
            MethodEmissionContext context,
            InteropImportPlan interop) => Count++;
    }

    private sealed class RecordingCallbackHandleEmitter : IHostCallbackHandleEmitter
    {
        public int Count { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            int delegateLocal,
            MethodEmissionContext context) => Count++;
    }

    private sealed class RecordingHandleReleaser : IInteropHandleReleaser
    {
        public int Count { get; private set; }

        public void Release(
            IWasmInstructionWriter code,
            MethodEmissionContext context,
            InteropMarshallingTarget target) => Count++;

        public void Release(
            IWasmInstructionWriter code,
            MethodEmissionContext context) => Count++;
    }

    private sealed class RecordingResultEmitter : IJavaScriptImportResultEmitter
    {
        public JavaScriptImportResultRequest? Request { get; private set; }

        public void Emit(
            JavaScriptImportResultRequest request,
            IWasmInstructionWriter code) => Request = request;
    }

    private sealed class RecordingExceptionEmitter : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind, OptionalFunctionIndex throwFunctionIndex) =>
            Kinds.Add(kind);
    }
}
