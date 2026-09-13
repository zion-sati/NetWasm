using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.Tests.EmitterTestSupport;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RuntimeIntrinsicCallEmitterTests
{
    [Fact]
    public void RegistryRejectsNullRegistrationSequence()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RuntimeIntrinsicEmitterRegistry(null!));
    }

    [Fact]
    public void RegistryRejectsNullRegistration()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RuntimeIntrinsicEmitterRegistry(
                new RuntimeIntrinsicEmitterRegistration[] { null! }));
    }

    [Fact]
    public void RegistryRejectsRegistrationWithNullEmitter()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RuntimeIntrinsicEmitterRegistry(
                [new RuntimeIntrinsicEmitterRegistration(
                    RuntimeIntrinsic.StringLength,
                    null!)]));
    }

    [Fact]
    public void RegistryRejectsDuplicateIntrinsicOwnershipDeterministically()
    {
        var registrations = Enum.GetValues<RuntimeIntrinsic>()
            .Select(intrinsic => new RuntimeIntrinsicEmitterRegistration(
                intrinsic,
                new RecordingEmitter()))
            .Append(new RuntimeIntrinsicEmitterRegistration(
                RuntimeIntrinsic.StringLength,
                new RecordingEmitter()));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeIntrinsicEmitterRegistry(registrations));

        Assert.Contains(nameof(RuntimeIntrinsic.StringLength), exception.Message);
    }

    [Fact]
    public void RegistryRejectsMissingIntrinsicOwnershipDeterministically()
    {
        var registrations = Enum.GetValues<RuntimeIntrinsic>()
            .Where(intrinsic => intrinsic != RuntimeIntrinsic.StringLength)
            .Select(intrinsic => new RuntimeIntrinsicEmitterRegistration(
                intrinsic,
                new RecordingEmitter()));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeIntrinsicEmitterRegistry(registrations));

        Assert.Contains(nameof(RuntimeIntrinsic.StringLength), exception.Message);
    }

    [Fact]
    public void RegistryResolvesTheEmitterRegisteredForAnIntrinsic()
    {
        var emitter = new RecordingEmitter();
        var registrations = Enum.GetValues<RuntimeIntrinsic>()
            .Select(intrinsic => new RuntimeIntrinsicEmitterRegistration(
                intrinsic,
                intrinsic == RuntimeIntrinsic.StringLength
                    ? emitter
                    : new RecordingEmitter()));
        var registry = new RuntimeIntrinsicEmitterRegistry(registrations);

        Assert.Same(
            emitter,
            GetThroughCapability(registry, RuntimeIntrinsic.StringLength));
    }

    [Fact]
    public void CallFacadeDelegatesThroughItsCapabilityAndUpdatesTheStack()
    {
        var program = new FakeProgram();
        var definition = program.GetMethod(StringLengthKey);
        var method = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(Assembly, "Test", "String", isValueType: false),
            [],
            definition.Signature);
        var instruction = EmitterTestSupport.CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.ManagedReference]);
        var call = new CallEmissionRequest(instruction, method, 0, 1);
        var registrations = Enum.GetValues<RuntimeIntrinsic>()
            .Select(intrinsic => new RuntimeIntrinsicEmitterRegistration(
                intrinsic,
                new RecordingEmitter()));
        var facade = new RuntimeIntrinsicCallEmitter(
            new FakeIntrinsics(),
            new RecordingLayoutProvider(),
            new RuntimeIntrinsicEmitterRegistry(registrations));

        EmitThroughCapability(facade, call);

        Assert.Equal([CliValueKind.I4], instruction.Stack);
    }

    [Fact]
    public void CallFacadeRejectsMethodsOutsideTheRuntimeIntrinsicRegistry()
    {
        var program = new FakeProgram();
        var definition = program.GetMethod(EntryKey);
        var method = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            [],
            definition.Signature);
        var instruction = EmitterTestSupport.CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.I4]);
        var call = new CallEmissionRequest(instruction, method, 0, 1);
        var registrations = Enum.GetValues<RuntimeIntrinsic>()
            .Select(intrinsic => new RuntimeIntrinsicEmitterRegistration(
                intrinsic,
                new RecordingEmitter()));
        var facade = new RuntimeIntrinsicCallEmitter(
            new FakeIntrinsics(),
            new RecordingLayoutProvider(),
            new RuntimeIntrinsicEmitterRegistry(registrations));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EmitThroughCapability(facade, call));

        Assert.Contains("is not a runtime intrinsic", exception.Message);
    }

    private static void EmitThroughCapability(
        RuntimeIntrinsicCallEmitter emitter,
        CallEmissionRequest request) =>
        ((ICallEmitter)emitter).Emit(
            request,
            GetCodeWriter(request.Instruction),
            EmitterTestSupport.CreateFunctionIndexResolver());

    private static IRuntimeIntrinsicEmitter GetThroughCapability(
        RuntimeIntrinsicEmitterRegistry registry,
        RuntimeIntrinsic intrinsic) =>
        ((IRuntimeIntrinsicEmitterRegistry)registry).Get(intrinsic);

    private sealed class RecordingEmitter : IRuntimeIntrinsicEmitter
    {
        public void Emit(
            RuntimeIntrinsicEmissionRequest request,
            IWasmInstructionWriter code)
        {
        }
    }
}
