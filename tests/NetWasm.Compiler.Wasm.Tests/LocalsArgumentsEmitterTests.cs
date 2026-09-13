using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class LocalsArgumentsEmitterTests
{
    [Fact]
    public void OrdinaryArgumentAndLocalRoundTripThroughWasmLocals()
    {
        var emitter = CreateEmitter();
        var context = CreateContext();
        var body = CreateBody();
        var stack = new List<CliValueKind>();
        var code = new RecordingInstructionWriter();

        Emit(emitter, Request(body, CilOperation.LoadArgument, 0, stack, context), code);
        Emit(emitter, Request(body, CilOperation.StoreLocal, 0, stack, context), code);
        Emit(emitter, Request(body, CilOperation.LoadLocal, 0, stack, context), code);
        Emit(emitter, Request(body, CilOperation.StoreArgument, 0, stack, context), code);

        Assert.Empty(stack);
        Assert.Equal(
            [
            WasmOpcodes.LocalGet, 0, WasmOpcodes.LocalSet, 2,
                WasmOpcodes.LocalGet, 2, WasmOpcodes.LocalSet, 1,
                WasmOpcodes.LocalGet, 1, WasmOpcodes.LocalSet, 2,
                WasmOpcodes.LocalGet, 2, WasmOpcodes.LocalSet, 0,
            ],
            code.ToArray());
    }

    [Fact]
    public void AddressTakenScalarWithoutSpillFailsWithMethodAndIlOffset()
    {
        var code = new RecordingInstructionWriter();
        var request = Request(
            CreateBody(),
            CilOperation.LoadLocalAddress,
            0,
            [],
            CreateContext());

        var exception = Assert.Throws<CompilerException>(() =>
            Emit(CreateEmitter(), request, code));

        Assert.Equal(DiagnosticCode.UnsupportedCil, exception.Diagnostic.Code);
        Assert.Contains("Test.Type::Run at IL_0000", exception.Message);
        Assert.Contains("address-taking scalar local", exception.Message);
    }

    [Fact]
    public void FilterCaptureUsesEnvironmentInsteadOfOriginalLocal()
    {
        var capture = new FilterCapture(
            new CapturedSlot(false, 0),
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            4,
            []);
        var context = CreateContext() with
        {
            ValueFrame = 30,
            FilterEnvironment = new FilterEnvironmentLayout(
                8,
                0,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(
                    capture.Slot,
                    capture)),
        };
        var code = new RecordingInstructionWriter();
        var request = Request(
            CreateBody(),
            CilOperation.LoadLocal,
            0,
            [],
            context);

        Emit(CreateEmitter(), request, code);

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.Equal(WasmOpcodes.LocalGet, code.ToArray()[0]);
        Assert.Equal(30, code.ToArray()[1]);
    }

    [Theory]
    [InlineData(CilOperation.LoadLocalAddress, false)]
    [InlineData(CilOperation.LoadArgumentAddress, true)]
    public void FilterCaptureAddressUsesTheEnvironment(
        CilOperation operation,
        bool isArgument)
    {
        var capture = new FilterCapture(
            new CapturedSlot(isArgument, 0),
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            4,
            []);
        var context = CreateContext() with
        {
            ValueFrame = 30,
            FilterEnvironment = new FilterEnvironmentLayout(
                8,
                0,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(
                    capture.Slot,
                    capture)),
        };
        var request = Request(CreateBody(), operation, 0, [], context);

        Emit(CreateEmitter(), request, new RecordingInstructionWriter());

        Assert.Equal([CliValueKind.ManagedAddress], request.Stack);
    }

    [Theory]
    [InlineData(CilOperation.StoreLocal, false)]
    [InlineData(CilOperation.StoreArgument, true)]
    public void FilterCaptureStoreUpdatesTheEnvironment(
        CilOperation operation,
        bool isArgument)
    {
        var capture = new FilterCapture(
            new CapturedSlot(isArgument, 0),
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            4,
            []);
        var context = CreateContext() with
        {
            ValueFrame = 30,
            FilterEnvironment = new FilterEnvironmentLayout(
                8,
                0,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(
                    capture.Slot,
                    capture)),
        };
        var request = Request(
            CreateBody(),
            operation,
            0,
            [CliValueKind.I4],
            context);

        Emit(CreateEmitter(), request, new RecordingInstructionWriter());

        Assert.Empty(request.Stack);
    }

    [Fact]
    public void ValueTypeArgumentStoreCopiesTheValueBlock()
    {
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true,
            CliValueKind.ValueType);
        var request = Request(
            CreateBody(),
            CilOperation.StoreArgument,
            0,
            [CliValueKind.ValueType],
            CreateContext());
        var code = new RecordingInstructionWriter();

        Emit(CreateEmitter(valueType), request, code);

        Assert.Empty(request.Stack);
        Assert.Contains(WasmOpcodes.Prefixed, code.ToArray());
    }

    [Fact]
    public void ScalarArgumentAddressWithoutSpillFailsWithMethodAndOffset()
    {
        var request = Request(
            CreateBody(),
            CilOperation.LoadArgumentAddress,
            0,
            [],
            CreateContext());

        var exception = Assert.Throws<CompilerException>(() =>
            Emit(CreateEmitter(), request, new RecordingInstructionWriter()));

        Assert.Equal(DiagnosticCode.UnsupportedCil, exception.Diagnostic.Code);
        Assert.Contains("address-taking a scalar argument", exception.Message);
    }

    [Fact]
    public void ValueArgumentsAndLocalsUseTheirValueFrames()
    {
        var valueType = CreateValueType();
        var body = CreateBody() with
        {
            LocalSignatureTypes = [valueType],
        };
        var context = CreateContext() with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                ImmutableDictionary<int, int>.Empty.Add(0, 8),
                ImmutableDictionary<int, int>.Empty.Add(0, 12),
                ImmutableDictionary<int, int>.Empty.Add(0, 4),
                ImmutableHashSet<int>.Empty.Add(0)),
        };
        var emitter = CreateEmitter(valueType);

        var loadArgument = Request(body, CilOperation.LoadArgument, 0, [], context);
        Emit(emitter, loadArgument, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ValueType], loadArgument.Stack);

        var storeArgument = Request(
            body,
            CilOperation.StoreArgument,
            0,
            [CliValueKind.ValueType],
            context);
        Emit(emitter, storeArgument, new RecordingInstructionWriter());
        Assert.Empty(storeArgument.Stack);

        var loadLocal = Request(body, CilOperation.LoadLocal, 0, [], context);
        Emit(emitter, loadLocal, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ValueType], loadLocal.Stack);

        var storeLocal = Request(
            body,
            CilOperation.StoreLocal,
            0,
            [CliValueKind.ValueType],
            context);
        Emit(emitter, storeLocal, new RecordingInstructionWriter());
        Assert.Empty(storeLocal.Stack);
    }

    [Fact]
    public void SpilledScalarLocalsAndArgumentsUseTheirFrameOffsets()
    {
        var body = CreateBody();
        var context = CreateContext() with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                ImmutableDictionary<int, int>.Empty,
                ImmutableDictionary<int, int>.Empty.Add(0, 12),
                ImmutableDictionary<int, int>.Empty,
                ImmutableHashSet<int>.Empty.Add(0)),
        };
        var emitter = CreateEmitter();

        var loadArgument = Request(body, CilOperation.LoadArgument, 0, [], context);
        Emit(emitter, loadArgument, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.I4], loadArgument.Stack);

        var storeArgument = Request(body, CilOperation.StoreArgument, 0, [CliValueKind.I4], context);
        Emit(emitter, storeArgument, new RecordingInstructionWriter());
        Assert.Empty(storeArgument.Stack);

        var loadLocal = Request(body, CilOperation.LoadLocal, 0, [], context);
        Emit(emitter, loadLocal, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.I4], loadLocal.Stack);

        var storeLocal = Request(body, CilOperation.StoreLocal, 0, [CliValueKind.I4], context);
        Emit(emitter, storeLocal, new RecordingInstructionWriter());
        Assert.Empty(storeLocal.Stack);
    }

    [Fact]
    public void Memory64StoresCompatiblePointersFromTheirActualNativeIntegerLocal()
    {
        var declared = CliTypeIdentity.FromStackKind(CliValueKind.ManagedAddress);
        var body = CreateBody() with { LocalSignatureTypes = [declared] };
        var baseContext = CreateContext();
        var nativeLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            baseContext.StackLocals,
            0,
            CliValueKind.NativeInt,
            WasmTargetLayout.Wasm64);
        var emitter = CreateEmitter(WasmTargetLayout.Wasm64, declared);

        var spilledContext = baseContext with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                ImmutableDictionary<int, int>.Empty,
                ImmutableDictionary<int, int>.Empty.Add(0, 8),
                ImmutableDictionary<int, int>.Empty,
                ImmutableHashSet<int>.Empty.Add(0)),
        };
        AssertReadsLocal(
            emitter,
            Request(body, CilOperation.StoreLocal, 0, [CliValueKind.NativeInt], spilledContext),
            nativeLocal);
        AssertReadsLocal(
            emitter,
            Request(body, CilOperation.StoreArgument, 0, [CliValueKind.NativeInt], spilledContext),
            nativeLocal);

        var capture = new FilterCapture(
            new CapturedSlot(false, 0),
            declared,
            4,
            []);
        var captureContext = baseContext with
        {
            FilterEnvironment = new FilterEnvironmentLayout(
                8,
                0,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(capture.Slot, capture)),
        };
        AssertReadsLocal(
            emitter,
            Request(body, CilOperation.StoreLocal, 0, [CliValueKind.NativeInt], captureContext),
            nativeLocal);
    }

    [Fact]
    public void AddressTakingSupportsValueTypesAndSpilledScalars()
    {
        var valueType = CreateValueType();
        var valueBody = CreateBody() with
        {
            LocalSignatureTypes = [valueType],
        };
        var valueRequest = Request(
            valueBody,
            CilOperation.LoadLocalAddress,
            0,
            [],
            CreateContext());
        var emitter = CreateEmitter(valueType);

        Emit(emitter, valueRequest, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ManagedAddress], valueRequest.Stack);

        var spilledRequest = Request(
            CreateBody(),
            CilOperation.LoadLocalAddress,
            0,
            [],
            CreateContext() with
            {
                ValueLayout = new ValueFrameLayout(
                    8,
                    [],
                    [],
                    [],
                    ImmutableHashSet<int>.Empty.Add(0)),
            });

        Emit(CreateEmitter(), spilledRequest, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ManagedAddress], spilledRequest.Stack);
    }

    [Fact]
    public void ArgumentAddressSupportsSpillsAndMethodInstanceReturnWidth()
    {
        var context = CreateContext() with
        {
            ValueLayout = new ValueFrameLayout(
                8,
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 4),
                [],
                []),
        };
        var offsetRequest = Request(
            CreateBody(),
            CilOperation.LoadArgumentAddress,
            0,
            [],
            context);
        Emit(CreateEmitter(), offsetRequest, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ManagedAddress], offsetRequest.Stack);

        var valueType = CreateValueType();
        var method = new FakeProgram().GetMethod(EntryKey);
        var instance = new MethodInstanceModel(
            method,
            valueType,
            [],
            MethodSignatureModel.Create(CliValueKind.ValueType, CliValueKind.I4));
        var methodInstanceRequest = Request(
            CreateBody() with { MethodInstance = instance },
            CilOperation.LoadArgumentAddress,
            0,
            [],
            CreateContext());
        Emit(CreateEmitter(valueType),
            methodInstanceRequest,
            new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ManagedAddress], methodInstanceRequest.Stack);

        var managedAddressRequest = Request(
            CreateBody(),
            CilOperation.LoadArgumentAddress,
            0,
            [],
            CreateContext());
        Emit(CreateEmitter(CliTypeIdentity.FromStackKind(CliValueKind.ManagedAddress)),
            managedAddressRequest,
            new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ManagedAddress], managedAddressRequest.Stack);
    }

    [Fact]
    public void ValueFilterCapturesLoadAndStoreWithoutScalarMemoryConversions()
    {
        var valueType = CreateValueType();
        var capture = new FilterCapture(
            new CapturedSlot(false, 0),
            valueType,
            4,
            []);
        var context = CreateContext() with
        {
            FilterEnvironment = new FilterEnvironmentLayout(
                8,
                0,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(
                    capture.Slot,
                    capture)),
        };
        var body = CreateBody() with { LocalSignatureTypes = [valueType] };
        var emitter = CreateEmitter(valueType);

        var load = Request(body, CilOperation.LoadLocal, 0, [], context);
        Emit(emitter, load, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ValueType], load.Stack);

        var store = Request(body, CilOperation.StoreLocal, 0, [CliValueKind.ValueType], context);
        Emit(emitter, store, new RecordingInstructionWriter());
        Assert.Empty(store.Stack);

        var argumentCapture = capture with { Slot = new CapturedSlot(true, 0) };
        var argumentContext = CreateContext() with
        {
            FilterEnvironment = new FilterEnvironmentLayout(
                8,
                0,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(
                    argumentCapture.Slot,
                    argumentCapture)),
        };
        var loadArgument = Request(
            body,
            CilOperation.LoadArgument,
            0,
            [],
            argumentContext);
        Emit(emitter, loadArgument, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ValueType], loadArgument.Stack);
    }

    [Fact]
    public void Memory64ValueFramesAndAddressConstantsUseI64Instructions()
    {
        var valueType = CreateValueType();
        var emitter = CreateEmitter(WasmTargetLayout.Wasm64, valueType);
        var body = CreateBody();
        var context = CreateContext() with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 8),
                []),
        };
        var request = Request(body, CilOperation.LoadArgument, 0, [], context);

        Emit(emitter, request, new RecordingInstructionWriter());

        Assert.Equal([CliValueKind.ValueType], request.Stack);
    }

    [Fact]
    public void FrameAddressContractsValidateInputsAndSupportZeroOffsets()
    {
        IValueFrameAddressEmitter offsets =
            CreateValueFrameAddresses(new RecordingLayoutProvider(WasmTargetLayout.Wasm64));
        IValueFrameAddressEmitter captures =
            CreateValueFrameAddresses(new RecordingLayoutProvider());
        var context = CreateContext();
        var code = new RecordingInstructionWriter();

        offsets.Emit(code, context, 0);
        Assert.Throws<ArgumentNullException>(() => offsets.Emit(null!, context, 0));
        Assert.Throws<ArgumentNullException>(() => offsets.Emit(code, null!, 0));
        Assert.Throws<ArgumentNullException>(() => captures.Emit(code, context, null!));
    }

    private static IInstructionCommandProvider CreateEmitter()
        => CreateEmitter(WasmTargetLayout.Wasm32);

    private static IInstructionCommandProvider CreateEmitter(WasmTargetLayout target)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(target);
        var argumentTypes = CreateArgumentTypes(program);
        var argumentSignatureTypes = CreateArgumentSignatureTypes(program);
        return AsProvider(new LocalsArgumentsEmitter(
            program,
            layouts,
            layouts,
            argumentTypes,
            argumentSignatureTypes,
            CreateValueFrameAddresses(layouts),
            CreateFilterEnvironmentRoots(layouts)));
    }

    private static IInstructionCommandProvider CreateEmitter(CliTypeIdentity argumentType)
        => CreateEmitter(WasmTargetLayout.Wasm32, argumentType);

    private static IInstructionCommandProvider CreateEmitter(
        WasmTargetLayout target,
        CliTypeIdentity argumentType)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(target);
        var resolver = new FixedArgumentTypeResolver(argumentType);
        return AsProvider(new LocalsArgumentsEmitter(
            program,
            layouts,
            layouts,
            resolver,
            resolver,
            CreateValueFrameAddresses(layouts),
            CreateFilterEnvironmentRoots(layouts)));
    }

    private static CliTypeIdentity CreateValueType() => CliTypeIdentity.Named(
        Assembly,
        "Test",
        "Value",
        isValueType: true,
        CliValueKind.ValueType);

    private static void Emit(
        IInstructionCommandProvider provider,
        InstructionEmissionRequest request,
        IWasmInstructionWriter code)
    {
        var command = provider.Commands.Single(candidate =>
            candidate.Operation == request.Instruction.Operation);
        command.Emit(request, code, CreateFunctionIndexResolver());
    }

    private static IInstructionCommandProvider AsProvider(
        IInstructionCommandProvider provider) => provider;

    private static void AssertReadsLocal(
        IInstructionCommandProvider emitter,
        InstructionEmissionRequest request,
        int local)
    {
        var code = new RecordingInstructionWriter();

        Emit(emitter, request, code);

        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand.UnsignedValue == (uint)local);
    }

    private static CilMethodBody CreateBody()
    {
        var program = new FakeProgram();
        return new CilMethodBody(
            program.GetMethod(EntryKey),
            1,
            [CliValueKind.I4],
            [])
        {
            LocalSignatureTypes =
                [CliTypeIdentity.FromStackKind(CliValueKind.I4)],
        };
    }

    private static InstructionEmissionRequest Request(
        CilMethodBody body,
        CilOperation operation,
        int index,
        List<CliValueKind> stack,
        MethodEmissionContext context) => new(
        Header(body),
        I(0, operation, new CilOperand.Index(index)),
        stack,
        context,
        CreateInstructionModuleTarget());

    private static MethodEmissionContext CreateContext()
    {
        var roots = new MethodRootMap(
            EntryKey,
            [],
            []);
        return new MethodEmissionContext(
            roots,
            1,
            2,
            WasmLocalLayoutPlanner.CreateEvaluationStack(2, 1),
            8,
            9,
            10,
            ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
            ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
            11,
            new ValueFrameLayout(
                0,
                [],
                [],
                [],
                []),
            12,
            FilterEnvironmentLayout.Empty,
            0,
            13,
            14,
            15,
            16,
            17,
            18);
    }

    private sealed class FixedArgumentTypeResolver(CliTypeIdentity type) :
        IArgumentTypeResolver,
        IArgumentSignatureTypeResolver
    {
        public CliValueKind Resolve(StructuredMethodHeader header, int index) =>
            type.StackKind;

        CliTypeIdentity IArgumentSignatureTypeResolver.Resolve(
            StructuredMethodHeader header,
            int index) => type;
    }
}
