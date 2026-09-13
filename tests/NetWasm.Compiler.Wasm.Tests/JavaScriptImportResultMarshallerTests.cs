using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class JavaScriptImportResultMarshallerTests
{
    [Theory]
    [InlineData(CliValueKind.I4, false, WasmOpcodes.I32Load)]
    [InlineData(CliValueKind.I8, false, WasmOpcodes.I64Load)]
    [InlineData(CliValueKind.F4, false, WasmOpcodes.F32Load)]
    [InlineData(CliValueKind.F8, false, WasmOpcodes.F64Load)]
    [InlineData(CliValueKind.NativeInt, false, WasmOpcodes.I32Load)]
    [InlineData(CliValueKind.NativeInt, true, WasmOpcodes.I64Load)]
    public void ScalarEmitterLoadsEverySupportedResultAndReplacesArguments(
        CliValueKind result,
        bool memory64,
        byte expectedLoad)
    {
        var stack = new List<CliValueKind> { CliValueKind.I4, CliValueKind.I8 };
        var locals = new FixedStackLocalResolver(19);
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32;
        IJavaScriptImportResultEmitter emitter =
            new[]
            {
                new ScalarJavaScriptImportResultMarshaller(
                    new RecordingLayoutProvider(target),
                    new FixedRuntimeImportResolver(23),
                    locals),
            }
            .Cast<IJavaScriptImportResultEmitter>()
            .Single();

        emitter.Emit(CreateRequest(result, stack, consumed: 2), code);

        Assert.Equal([result], stack);
        Assert.Equal((0, result), locals.Request);
        Assert.Contains(expectedLoad, code.ToArray());
        Assert.Contains(WasmOpcodes.Call, code.ToArray());
    }

    [Fact]
    public void ScalarVoidResultLeavesNoStackValueAndDoesNotResolveDestination()
    {
        var stack = new List<CliValueKind> { CliValueKind.I4 };
        var locals = new FixedStackLocalResolver(19);
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        IJavaScriptImportResultEmitter emitter =
            new[]
            {
                new ScalarJavaScriptImportResultMarshaller(
                    new RecordingLayoutProvider(),
                    new FixedRuntimeImportResolver(23),
                    locals),
            }
            .Cast<IJavaScriptImportResultEmitter>()
            .Single();

        emitter.Emit(CreateRequest(CliValueKind.Void, stack, consumed: 1), code);

        Assert.Empty(stack);
        Assert.Null(locals.Request);
        Assert.DoesNotContain(WasmOpcodes.LocalSet, code.ToArray());
        Assert.Contains(WasmOpcodes.Call, code.ToArray());
    }

    [Fact]
    public void ScalarEmitterRejectsNonScalarResult()
    {
        IJavaScriptImportResultEmitter emitter =
            new[]
            {
                new ScalarJavaScriptImportResultMarshaller(
                    new RecordingLayoutProvider(),
                    new FixedRuntimeImportResolver(23),
                    new FixedStackLocalResolver(19)),
            }
            .Cast<IJavaScriptImportResultEmitter>()
            .Single();

        var exception = Assert.Throws<InvalidOperationException>(() => emitter.Emit(
            CreateRequest(CliValueKind.ManagedReference, [], consumed: 0),
            new EmitterTestSupport.RecordingInstructionWriter()));

        Assert.Equal("unsupported scalar host result type", exception.Message);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 1)]
    public void HostObjectEmitterOwnsNullCallbackAndSubscriptionPaths(
        bool hasCallback,
        bool subscription,
        int expectedContextReleaseCount)
    {
        var type = CliTypeIdentity.Named(
            new AssemblyIdentity("Tests"),
            "System.Runtime.InteropServices.JavaScript",
            subscription ? "JSSubscription" : "JSObject",
            isValueType: false);
        var stack = new List<CliValueKind> { CliValueKind.ManagedReference };
        var handles = new RecordingInteropHandleReleaser();
        var allocations = new RecordingAllocationResultValidator();
        var captures = new RecordingJavaScriptResultHandleCapturer();
        var addresses = new RecordingAddressInstructionEmitter();
        var functionIndices = new RecordingOptionalFunctionIndexValidator();
        var callback = hasCallback ? CreateCallback() : null;
        var target = CreateInteropTarget();
        IJavaScriptImportResultEmitter emitter = new[]
        {
            new HostObjectJavaScriptImportResultMarshaller(
                new RecordingLayoutProvider(),
                new RecordingLayoutProvider(),
                new FixedRuntimeImportResolver(23),
                functionIndices,
                captures,
                new FixedStackLocalResolver(19),
                handles,
                allocations,
                addresses),
        }.Cast<IJavaScriptImportResultEmitter>().Single();

        emitter.Emit(
            CreateRequest(type, stack, consumed: 1, callback, target),
            new EmitterTestSupport.RecordingInstructionWriter());

        Assert.Equal([CliValueKind.ManagedReference], stack);
        Assert.Equal(1, captures.Count);
        Assert.Equal(expectedContextReleaseCount, handles.ContextReleaseCount);
        Assert.Equal(0, handles.TargetReleaseCount);
        Assert.Equal(1, allocations.Count);
        Assert.Equal(hasCallback && subscription ? 6 : 4, addresses.Count);
        Assert.Single(functionIndices.Requests);
    }

    [Fact]
    public void StringEmitterCoordinatesEveryFocusedResultAction()
    {
        var type = CliTypeIdentity.Primitive(
            "string",
            CliValueKind.ManagedReference,
            isValueType: false);
        var stack = new List<CliValueKind> { CliValueKind.I4 };
        var handles = new RecordingInteropHandleReleaser();
        var allocations = new RecordingAllocationResultValidator();
        var captures = new RecordingJavaScriptResultHandleCapturer();
        var addresses = new RecordingAddressInstructionEmitter();
        var functionIndices = new RecordingOptionalFunctionIndexValidator();
        var lengths = new RecordingJavaScriptResultLengthValidator();
        var exceptions = new RecordingImplicitExceptionEmitter();
        IJavaScriptImportResultEmitter emitter = new[]
        {
            new StringJavaScriptImportResultMarshaller(
                new RecordingLayoutProvider(),
                new FixedRuntimeImportResolver(23),
                exceptions,
                functionIndices,
                captures,
                new FixedStackLocalResolver(19),
                lengths,
                handles,
                allocations,
                addresses),
        }.Cast<IJavaScriptImportResultEmitter>().Single();

        emitter.Emit(
            CreateRequest(type, stack, consumed: 1, null, CreateInteropTarget()),
            new EmitterTestSupport.RecordingInstructionWriter());

        Assert.Equal([CliValueKind.ManagedReference], stack);
        Assert.Equal(3, functionIndices.Requests.Count);
        Assert.Equal(1, captures.Count);
        Assert.Equal(1, lengths.Count);
        Assert.Equal(3, handles.TargetReleaseCount);
        Assert.Equal(1, allocations.Count);
        Assert.Equal(1, addresses.Count);
        Assert.Equal(
            [ManagedExceptionKind.OutOfMemory, ManagedExceptionKind.JSException],
            exceptions.Kinds);
    }

    [Fact]
    public void ByteArrayEmitterCoordinatesEveryFocusedResultAction()
    {
        var type = CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("u1", CliValueKind.I4));
        var stack = new List<CliValueKind> { CliValueKind.I8 };
        var handles = new RecordingInteropHandleReleaser();
        var allocations = new RecordingAllocationResultValidator();
        var captures = new RecordingJavaScriptResultHandleCapturer();
        var addresses = new RecordingAddressInstructionEmitter();
        var functionIndices = new RecordingOptionalFunctionIndexValidator();
        var lengths = new RecordingJavaScriptResultLengthValidator();
        var exceptions = new RecordingImplicitExceptionEmitter();
        var layouts = new RecordingLayoutProvider();
        IJavaScriptImportResultEmitter emitter = new[]
        {
            new ByteArrayJavaScriptImportResultMarshaller(
                layouts,
                layouts,
                new FixedRuntimeImportResolver(23),
                exceptions,
                functionIndices,
                captures,
                new FixedStackLocalResolver(19),
                lengths,
                handles,
                allocations,
                addresses),
        }.Cast<IJavaScriptImportResultEmitter>().Single();

        emitter.Emit(
            CreateRequest(type, stack, consumed: 1, null, CreateInteropTarget()),
            new EmitterTestSupport.RecordingInstructionWriter());

        Assert.Equal([CliValueKind.ManagedReference], stack);
        Assert.Equal(3, functionIndices.Requests.Count);
        Assert.Equal(1, captures.Count);
        Assert.Equal(1, lengths.Count);
        Assert.Equal(2, handles.TargetReleaseCount);
        Assert.Equal(1, allocations.Count);
        Assert.Equal(1, addresses.Count);
        Assert.Equal([ManagedExceptionKind.JSException], exceptions.Kinds);
    }

    [Fact]
    public void ResolverMapsEverySupportedTypeFamilyToAStableKind()
    {
        var resolver = new JavaScriptImportResultKindResolver();
        var stringType = CliTypeIdentity.Primitive(
            "string",
            CliValueKind.ManagedReference,
            isValueType: false);
        var byteArray = CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("u1", CliValueKind.I4));
        var hostObject = CliTypeIdentity.Named(
            new AssemblyIdentity("Tests"),
            "System.Runtime.InteropServices.JavaScript",
            "JSObject",
            isValueType: false);

        Assert.Equal(
            JavaScriptImportResultKind.String,
            ((IJavaScriptImportResultKindResolver)resolver).Resolve(stringType));
        Assert.Equal(
            JavaScriptImportResultKind.ByteArray,
            ((IJavaScriptImportResultKindResolver)resolver).Resolve(byteArray));
        Assert.Equal(
            JavaScriptImportResultKind.HostObject,
            ((IJavaScriptImportResultKindResolver)resolver).Resolve(hostObject));
        Assert.Equal(
            JavaScriptImportResultKind.Scalar,
            ((IJavaScriptImportResultKindResolver)resolver).Resolve(
                CliTypeIdentity.FromStackKind(CliValueKind.I4)));
    }

    [Fact]
    public void MarshallerDispatchesDirectlyToTheResolvedEmitter()
    {
        var selected = new RecordingEmitter();
        var registry = CreateRegistry(selected);
        var marshaller = new JavaScriptImportResultMarshaller(
            new FixedKindResolver(JavaScriptImportResultKind.String),
            registry);

        ((IJavaScriptImportResultEmitter)marshaller).Emit(
            CreateRequest(),
            new EmitterTestSupport.RecordingInstructionWriter());

        Assert.True(selected.Emitted);
    }

    [Fact]
    public void RegistryRejectsDuplicateKinds()
    {
        var registrations = Registrations(new RecordingEmitter()).ToList();
        registrations.Add(new(
            JavaScriptImportResultKind.String,
            new RecordingEmitter()));

        Assert.Throws<InvalidOperationException>(() =>
            new JavaScriptImportResultEmitterRegistry(registrations));
    }

    [Fact]
    public void RegistryRejectsMissingKinds()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new JavaScriptImportResultEmitterRegistry(
                Registrations(new RecordingEmitter())
                    .Where(registration =>
                        registration.Kind != JavaScriptImportResultKind.HostObject)));
    }

    [Fact]
    public void RegistryRejectsUnknownKinds()
    {
        var registry = CreateRegistry(new RecordingEmitter());

        Assert.Throws<InvalidOperationException>(() => registry.Get(
            (JavaScriptImportResultKind)int.MaxValue));
    }

    private static JavaScriptImportResultEmitterRegistry CreateRegistry(
        IJavaScriptImportResultEmitter selected) =>
        new JavaScriptImportResultEmitterRegistry(Registrations(selected));

    private static IEnumerable<JavaScriptImportResultEmitterRegistration> Registrations(
        IJavaScriptImportResultEmitter selected)
    {
        foreach (var kind in Enum.GetValues<JavaScriptImportResultKind>())
        {
            yield return new JavaScriptImportResultEmitterRegistration(
                kind,
                kind == JavaScriptImportResultKind.String
                    ? selected
                    : new RecordingEmitter());
        }
    }

    private static JavaScriptImportResultRequest CreateRequest(
        CliValueKind result = CliValueKind.I4,
        List<CliValueKind>? stack = null,
        int consumed = 0) => new(
        MethodSignatureModel.Create(result),
        stack ?? [],
        EmitterTestSupport.CreateMethodEmissionContext(),
        0,
        consumed,
        null,
        new InteropMarshallingTarget(new InteropImportPlan(
            [], default, default, default, default, default, default)));

    private static JavaScriptImportResultRequest CreateRequest(
        CliTypeIdentity result,
        List<CliValueKind> stack,
        int consumed,
        HostCallbackDeclaration? callback,
        InteropMarshallingTarget target) => new(
        MethodSignatureModel.Create(result),
        stack,
        EmitterTestSupport.CreateMethodEmissionContext(),
        0,
        consumed,
        callback,
        target);

    private static InteropMarshallingTarget CreateInteropTarget() => new(
        new InteropImportPlan(
            [],
            OptionalFunctionIndex.At(30),
            OptionalFunctionIndex.At(31),
            OptionalFunctionIndex.At(32),
            OptionalFunctionIndex.At(33),
            OptionalFunctionIndex.At(34),
            OptionalFunctionIndex.At(35)));

    private static HostCallbackDeclaration CreateCallback()
    {
        var method = new FakeProgram().GetMethod(EmitterTestSupport.EntryKey);
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(
                new AssemblyIdentity("Tests"),
                "Tests",
                "Callback",
                isValueType: false),
            [],
            method.Signature);
        return new HostCallbackDeclaration(
            EmitterTestSupport.EntryKey,
            0,
            instance,
            "callback");
    }

    private sealed class FixedKindResolver(JavaScriptImportResultKind kind) :
        IJavaScriptImportResultKindResolver
    {
        public JavaScriptImportResultKind Resolve(CliTypeIdentity type) => kind;
    }

    private sealed class RecordingEmitter : IJavaScriptImportResultEmitter
    {
        public bool Emitted { get; private set; }

        public void Emit(
            JavaScriptImportResultRequest request,
            IWasmInstructionWriter code) => Emitted = true;
    }

    private sealed class FixedStackLocalResolver(int local) : IStackLocalResolver
    {
        public (int Slot, CliValueKind Type)? Request { get; private set; }

        public int Resolve(
            MethodEmissionContext context,
            int slot,
            CliValueKind type)
        {
            Request = (slot, type);
            return local;
        }
    }

    private sealed class FixedRuntimeImportResolver(int index) : IRuntimeImportResolver
    {
        public int Resolve(RuntimeImportSymbol symbol) => index;

        public int Resolve(RuntimeImportSymbol symbol, WasmModuleProfile profile) => index;

        public ImmutableArray<WasmFunctionImport> Resolve(WasmModuleProfile profile) => [];
    }

    private sealed class RecordingOptionalFunctionIndexValidator :
        IOptionalFunctionIndexValidator
    {
        public List<(OptionalFunctionIndex Index, string Message)> Requests { get; } = [];

        public void Validate(OptionalFunctionIndex index, string message) =>
            Requests.Add((index, message));
    }

    private sealed class RecordingJavaScriptResultHandleCapturer :
        IJavaScriptResultHandleCapturer
    {
        public int Count { get; private set; }

        public void Capture(
            IWasmInstructionWriter code,
            MethodEmissionContext context) => Count++;
    }

    private sealed class RecordingInteropHandleReleaser : IInteropHandleReleaser
    {
        public int ContextReleaseCount { get; private set; }
        public int TargetReleaseCount { get; private set; }

        public void Release(
            IWasmInstructionWriter code,
            MethodEmissionContext context,
            InteropMarshallingTarget target) => TargetReleaseCount++;

        public void Release(
            IWasmInstructionWriter code,
            MethodEmissionContext context) => ContextReleaseCount++;
    }

    private sealed class RecordingAllocationResultValidator : IAllocationResultValidator
    {
        public int Count { get; private set; }

        public void Validate(IWasmInstructionWriter code, int objectLocal) => Count++;
    }

    private sealed class RecordingAddressInstructionEmitter : IAddressInstructionEmitter
    {
        public int Count { get; private set; }

        public void Emit(IWasmInstructionWriter code, int constant) => Count++;

        public void Emit(IWasmInstructionWriter code, AddressOperation operation) => Count++;
    }

    private sealed class RecordingJavaScriptResultLengthValidator :
        IJavaScriptResultLengthValidator
    {
        public int Count { get; private set; }

        public void Validate(
            IWasmInstructionWriter code,
            MethodEmissionContext context,
            InteropMarshallingTarget target) => Count++;
    }

    private sealed class RecordingImplicitExceptionEmitter : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind) =>
            Kinds.Add(kind);
    }
}
