using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class CallableLoadingEmitterTests
{
    [Fact]
    public void LoadFunctionPushesCallableIndex()
    {
        var request = CreateInstructionRequest(
            CilOperation.LoadFunction,
            operand: new CilOperand.Entity(EntryKey));

        var emitter = CreateFunctionEmitter();
        LoadFunction(
            emitter,
            request,
            GetCodeWriter(request),
            CreateFunctionIndexResolver());

        Assert.Equal([CliValueKind.NativeInt], request.Stack);
        Assert.Contains(WasmOpcodes.I32Constant, GetCodeBytes(request));
    }

    [Fact]
    public void LoadVirtualFunctionTestsReceiverAndReplacesItWithCallableIndex()
    {
        var program = new FakeProgram();
        var caller = CreateMethodInstance(program);
        var site = new DispatchCallSiteModel(
            caller.CanonicalName,
            0,
            caller,
            [new DispatchTargetModel(caller.DeclaringType, caller)]);
        var instruction = I(
            0,
            CilOperation.LoadVirtualFunction,
            new CilOperand.Entity(EntryKey));
        var header = Header(new CilMethodBody(program.GetMethod(EntryKey), 3, [], [])
        {
            MethodInstance = caller,
        });
        var target = CreateInstructionModuleTarget(program) with
        {
            DispatchCallSites = ImmutableDictionary<string, DispatchCallSiteModel>.Empty.Add(
                $"{caller.CanonicalName}@00000000",
                site),
        };
        var code = new RecordingInstructionWriter();
        var request = new InstructionEmissionRequest(
            header,
            instruction,
            [CliValueKind.ManagedReference],
            CreateMethodEmissionContext(),
            target);

        var (emitter, _) = CreateVirtualFunctionEmitter();
        LoadVirtualFunction(
            emitter,
            request,
            code,
            CreateFunctionIndexResolver(program));

        Assert.Equal([CliValueKind.NativeInt], request.Stack);
        Assert.Contains(WasmOpcodes.If, code.ToArray());
        Assert.Contains(WasmOpcodes.Throw, code.ToArray());
    }

    [Fact]
    public void ExposesVirtualFunctionLoadCommandThroughProviderContract()
    {
        var (_, provider) = CreateVirtualFunctionEmitter();

        var command = Assert.Single(provider.Commands);

        Assert.Equal(CilOperation.LoadVirtualFunction, command.Operation);
        Assert.Equal(InstructionFamily.CallsAndCallableLoading, command.Family);
    }

    [Fact]
    public void LoadsVirtualFunctionWithMemory64NullCheckAndCallableAddress()
    {
        var program = new FakeProgram();
        var caller = CreateMethodInstance(program);
        var site = new DispatchCallSiteModel(
            caller.CanonicalName,
            0,
            caller,
            [new DispatchTargetModel(caller.DeclaringType, caller)]);
        var instruction = I(
            0,
            CilOperation.LoadVirtualFunction,
            new CilOperand.Entity(EntryKey));
        var header = Header(new CilMethodBody(program.GetMethod(EntryKey), 3, [], [])
        {
            MethodInstance = caller,
        });
        var target = CreateInstructionModuleTarget(program) with
        {
            DispatchCallSites = ImmutableDictionary<string, DispatchCallSiteModel>.Empty.Add(
                site.Key,
                site),
        };
        var code = new RecordingInstructionWriter();
        var request = new InstructionEmissionRequest(
            header,
            instruction,
            [CliValueKind.ManagedReference],
            CreateMethodEmissionContext(),
            target);

        var (emitter, _) = CreateVirtualFunctionEmitter(WasmTarget.Wasm64);
        LoadVirtualFunction(
            emitter,
            request,
            code,
            CreateFunctionIndexResolver(program));

        Assert.Equal([CliValueKind.NativeInt], request.Stack);
        Assert.Contains(WasmOpcodes.I64EqualZero, code.ToArray());
        Assert.Contains(WasmOpcodes.I64ExtendI32Unsigned, code.ToArray());
    }

    [Fact]
    public void RejectsVirtualFunctionLoadWithoutMethodInstanceCaller()
    {
        var program = new FakeProgram();
        var instruction = I(
            0,
            CilOperation.LoadVirtualFunction,
            new CilOperand.Entity(EntryKey));
        var header = Header(new CilMethodBody(program.GetMethod(EntryKey), 3, [], []));
        var request = new InstructionEmissionRequest(
            header,
            instruction,
            [CliValueKind.ManagedReference],
            CreateMethodEmissionContext(),
            CreateInstructionModuleTarget(program));
        var code = new RecordingInstructionWriter();
        var (emitter, _) = CreateVirtualFunctionEmitter();

        var exception = Assert.Throws<InvalidOperationException>(() => LoadVirtualFunction(
            emitter,
            request,
            code,
            CreateFunctionIndexResolver(program)));

        Assert.Contains("method instance caller", exception.Message);
    }

    [Fact]
    public void RejectsVirtualFunctionLoadWithoutDispatchSite()
    {
        var program = new FakeProgram();
        var caller = CreateMethodInstance(program);
        var instruction = I(
            0,
            CilOperation.LoadVirtualFunction,
            new CilOperand.Entity(EntryKey));
        var header = Header(new CilMethodBody(program.GetMethod(EntryKey), 3, [], [])
        {
            MethodInstance = caller,
        });
        var request = new InstructionEmissionRequest(
            header,
            instruction,
            [CliValueKind.ManagedReference],
            CreateMethodEmissionContext(),
            CreateInstructionModuleTarget(program));
        var code = new RecordingInstructionWriter();
        var (emitter, _) = CreateVirtualFunctionEmitter();

        var exception = Assert.Throws<InvalidOperationException>(() => LoadVirtualFunction(
            emitter,
            request,
            code,
            CreateFunctionIndexResolver(program)));

        Assert.Contains("Missing virtual function-load site", exception.Message);
    }

    [Fact]
    public void CompatibilityFacadeRoutesEachOperationToItsDedicatedLoader()
    {
        var functions = new RecordingFunctionLoader();
        var virtualFunctions = new RecordingVirtualFunctionLoader();
        var emitter = new CallableLoadingEmitter(functions, virtualFunctions);

        var functionRequest = CreateInstructionRequest(CilOperation.LoadFunction);
        emitter.Commands
            .Single(command => command.Operation == functionRequest.Instruction.Operation)
            .Emit(
                functionRequest,
                GetCodeWriter(functionRequest),
                CreateFunctionIndexResolver());
        var virtualFunctionRequest = CreateInstructionRequest(
            CilOperation.LoadVirtualFunction,
            [CliValueKind.ManagedReference]);
        emitter.Commands
            .Single(command => command.Operation == virtualFunctionRequest.Instruction.Operation)
            .Emit(
                virtualFunctionRequest,
                GetCodeWriter(virtualFunctionRequest),
                CreateFunctionIndexResolver());

        Assert.Equal(1, functions.Loads);
        Assert.Equal(1, virtualFunctions.Loads);
    }

    private static FunctionLoadEmitter CreateFunctionEmitter()
    {
        var program = new FakeProgram();
        return new FunctionLoadEmitter(
            new RecordingLayoutProvider(),
            CreateMethodOperands(program),
            new InstructionCommandFactory());
    }

    private static (
        IVirtualFunctionLoader Loader,
        InstructionCommandProvider Provider) CreateVirtualFunctionEmitter(
            WasmTarget target = WasmTarget.Wasm32)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = new VirtualFunctionLoadEmitter(
            layouts,
            layouts,
            new ImplicitExceptionEmitter(layouts, layouts, 7),
            new InstructionCommandFactory());
        return (
            new[] { emitter }.Cast<IVirtualFunctionLoader>().Single(),
            new[] { emitter }.Cast<InstructionCommandProvider>().Single());
    }

    private static void LoadFunction<TLoader>(
        TLoader loader,
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
        where TLoader : IFunctionLoader => loader.Load(
            request,
            code,
            functionIndices);

    private static void LoadVirtualFunction<TLoader>(
        TLoader loader,
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
        where TLoader : IVirtualFunctionLoader => loader.Load(
            request,
            code,
            functionIndices);

    private static MethodInstanceModel CreateMethodInstance(FakeProgram program)
    {
        var method = program.GetMethod(EntryKey);
        return new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            [],
            method.Signature);
    }

    private sealed class RecordingFunctionLoader : IFunctionLoader
    {
        public int Loads { get; private set; }

        public void Load(
            InstructionEmissionRequest request,
            IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices) => Loads++;
    }

    private sealed class RecordingVirtualFunctionLoader : IVirtualFunctionLoader
    {
        public int Loads { get; private set; }

        public void Load(
            InstructionEmissionRequest request,
            IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices) => Loads++;
    }
}
