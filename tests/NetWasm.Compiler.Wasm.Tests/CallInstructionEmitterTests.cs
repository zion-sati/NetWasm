using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class CallInstructionEmitterTests
{
    [Fact]
    public void UsesTheEmitterRegisteredForTheResolvedKind()
    {
        var direct = new RecordingCallEmitter();
        var emitter = new CallInstructionEmitter(
            CreateMethodOperands(new FakeProgram()),
            new FixedCallEmissionKindResolver(CallEmissionKind.Direct),
            new CallEmissionRegistry(
                Enum.GetValues<CallEmissionKind>()
                    .Select(kind => new CallEmissionRegistration(kind, direct))));
        var functionIndices = CreateFunctionIndexResolver();
        var request = CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.I4],
            new CilOperand.Entity(EntryKey));

        var code = new RecordingInstructionWriter();
        emitter.Commands
            .Single(command => command.Operation == request.Instruction.Operation)
            .Emit(request, code, functionIndices);

        Assert.Equal(1, direct.Emissions);
        Assert.Same(functionIndices, direct.FunctionIndices);
    }

    [Fact]
    public void ResolvesConstrainedPrefixesOnlyFromAClosedTypeOperand()
    {
        var direct = new RecordingCallEmitter();
        var emitter = new CallInstructionEmitter(
            CreateMethodOperands(new FakeProgram()),
            new FixedCallEmissionKindResolver(CallEmissionKind.Direct),
            new CallEmissionRegistry(
                Enum.GetValues<CallEmissionKind>()
                    .Select(kind => new CallEmissionRegistration(kind, direct))));
        var constrainedType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true);

        Emit(emitter, CreateCallRequest(CilOperation.Nop, new CilOperand.None()));
        Assert.Null(direct.LastRequest!.ConstrainedType);

        Emit(emitter, CreateCallRequest(
            CilOperation.Constrained,
            new CilOperand.None()));
        Assert.Null(direct.LastRequest!.ConstrainedType);

        Emit(emitter, CreateCallRequest(
            CilOperation.Constrained,
            new CilOperand.TypeIdentity(constrainedType)));
        Assert.Same(constrainedType, direct.LastRequest!.ConstrainedType);
    }

    private static InstructionEmissionRequest CreateCallRequest(
        CilOperation prefixOperation,
        CilOperand prefixOperand)
    {
        var program = new FakeProgram();
        var prefix = I(0, prefixOperation, prefixOperand);
        var call = I(1, CilOperation.Call, new CilOperand.Entity(EntryKey));
        var request = new InstructionEmissionRequest(
            Header(new CilMethodBody(program.GetMethod(EntryKey), 3, [], [prefix, call])),
            call,
            [CliValueKind.I4],
            CreateMethodEmissionContext(),
            CreateInstructionModuleTarget(program));
        RegisterInstructionWriter(request, new RecordingInstructionWriter());
        return request;
    }

    private static void Emit(
        CallInstructionEmitter emitter,
        InstructionEmissionRequest request) => emitter.Commands
        .Single(command => command.Operation == request.Instruction.Operation)
        .Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());

    private sealed class FixedCallEmissionKindResolver(CallEmissionKind kind) :
        ICallEmissionKindResolver
    {
        public CallEmissionKind Resolve(
            CallEmissionRequest request,
            IWasmInstructionWriter code) => kind;
    }

    private sealed class RecordingCallEmitter : ICallEmitter
    {
        public int Emissions { get; private set; }
        public IFunctionIndexResolver? FunctionIndices { get; private set; }
        public CallEmissionRequest? LastRequest { get; private set; }

        public void Emit(
            CallEmissionRequest request,
            IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices)
        {
            Emissions++;
            FunctionIndices = functionIndices;
            LastRequest = request;
        }
    }
}
