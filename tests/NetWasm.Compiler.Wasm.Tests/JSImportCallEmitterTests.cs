using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class JSImportCallEmitterTests
{
    [Fact]
    public void AcceptsOnlyMethodsDeclaredAsJSImport()
    {
        var program = new FakeProgram();
        var ordinary = CreateMethodInstance(program.GetMethod(EntryKey));
        var imported = CreateMethodInstance(program.GetMethod(EntryKey) with
        {
            JSImport = new("run", "tests"),
        });
        var layouts = new RecordingLayoutProvider();
        var exceptions = new ImplicitExceptionEmitter(layouts, layouts, 7);
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var addresses = CreateAddressInstructions(layouts);
        var emitter = new JSImportCallEmitter(
            layouts,
            runtimeImports,
            new JavaScriptResultDescriptorSizeResolver(layouts),
            new JavaScriptImportArgumentEmitter(layouts, addresses),
            new HostCallbackHandleEmitter(runtimeImports),
            new InteropHandleReleaser(runtimeImports),
            CreateJavaScriptImportResults(layouts),
            exceptions);
        var instruction = CreateInstructionRequest(CilOperation.Call);
        var resolver = new CallEmissionKindResolver(program, new FakeIntrinsics());

        Assert.Equal(
            CallEmissionKind.Direct,
            resolver.Resolve(
                new(instruction, ordinary, 0, 0),
                GetCodeWriter(instruction)));
        Assert.Equal(
            CallEmissionKind.JavaScriptImport,
            resolver.Resolve(
                new(instruction, imported, 0, 0),
                GetCodeWriter(instruction)));
    }

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
        var definition = program.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4),
            JSImport = new("run", "tests"),
        };
        var method = CreateMethodInstance(definition);
        var context = CreateMethodEmissionContext();
        var instruction = CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            new CilOperand.Entity(EntryKey),
            context);
        if (hasCallback)
        {
            var callbackMethod = CreateMethodInstance(program.GetMethod(EntryKey));
            instruction = instruction with
            {
                Target = instruction.Target with
                {
                    HostCallbacks = ImmutableDictionary<(EntityKey, int), HostCallbackDeclaration>
                        .Empty.Add(
                            (EntryKey, 0),
                            new HostCallbackDeclaration(
                                EntryKey,
                                0,
                                callbackMethod,
                                "callback")),
                },
            };
        }
        var arguments = new RecordingArgumentEmitter();
        var callbackHandles = new RecordingCallbackHandleEmitter();
        var handles = new RecordingHandleReleaser();
        var results = new RecordingResultEmitter();
        var exceptions = new RecordingExceptionEmitter();
        ICallEmitter emitter = new[]
        {
            new global::NetWasm.Compiler.Wasm.Emission.Instructions.Interop.JSImportCallEmitter(
                new RecordingLayoutProvider(),
                WasmRuntimeImports.CreateCatalog(),
                new FixedDescriptorSizeResolver(16),
                arguments,
                callbackHandles,
                handles,
                results,
                exceptions),
        }.Cast<ICallEmitter>().Single();
        var code = new RecordingInstructionWriter();

        emitter.Emit(
            new CallEmissionRequest(instruction, method, 0, 2),
            code,
            CreateFunctionIndexResolver(program));

        Assert.Equal(expectedArgumentCount, arguments.Count);
        Assert.Equal(expectedCallbackCount, callbackHandles.Count);
        Assert.Equal(expectedReleaseCount, handles.Count);
        Assert.Equal([ManagedExceptionKind.JSException], exceptions.Kinds);
        Assert.NotNull(results.Request);
        Assert.Equal(hasCallback, results.Request.Callback is not null);
        Assert.Contains(WasmOpcodes.Call, code.ToArray());
    }

    private static MethodInstanceModel CreateMethodInstance(MethodDefinitionModel method) => new(
        method,
        CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
        [],
        method.Signature);

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
            MethodEmissionContext context) => Count++;
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

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind) =>
            Kinds.Add(kind);
    }
}
