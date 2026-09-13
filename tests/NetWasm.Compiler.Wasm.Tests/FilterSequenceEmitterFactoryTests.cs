using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class FilterSequenceEmitterFactoryTests
{
    [Fact]
    public void CreatesAnAdapterThatPassesTheOperationTargetAndResolver()
    {
        var target = CreateInstructionModuleTarget(new FakeProgram());
        var functionIndices = CreateFunctionIndexResolver();
        var sequences = new RecordingSequenceEmitter();
        var emitter = new FilterSequenceEmitterFactory(sequences)
            .Create(target, functionIndices);
        var program = new FakeProgram();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));

        emitter.Emit(
            new RecordingInstructionWriter(),
            method,
            StructuredSequence.Empty,
            CreateMethodEmissionContext());

        Assert.Same(target, sequences.Target);
        Assert.Same(functionIndices, sequences.FunctionIndices);
    }

    [Fact]
    public void RejectsMissingSequenceEmitter()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new FilterSequenceEmitterFactory(null!));
    }

    [Fact]
    public void RejectsMissingOperationTarget()
    {
        var factory = new FilterSequenceEmitterFactory(new RecordingSequenceEmitter());

        Assert.Throws<ArgumentNullException>(() =>
            factory.Create(null!, CreateFunctionIndexResolver()));
    }

    [Fact]
    public void RejectsMissingFunctionResolver()
    {
        var factory = new FilterSequenceEmitterFactory(new RecordingSequenceEmitter());

        Assert.Throws<ArgumentNullException>(() =>
            factory.Create(CreateInstructionModuleTarget(), null!));
    }

    private sealed class RecordingSequenceEmitter : IManagedMethodSequenceEmitter
    {
        public InstructionModuleTarget? Target { get; private set; }
        public IFunctionIndexResolver? FunctionIndices { get; private set; }

        public ManagedMethodSequenceEmission Emit(
            IWasmInstructionWriter code,
            StructuredMethod method,
            StructuredSequence sequence,
            MethodEmissionContext context,
            InstructionModuleTarget target,
            IFunctionIndexResolver functionIndices,
            int? loopBreakDepth = null,
            int? loopContinueDepth = null,
            int? exceptionLeaveDepth = null)
        {
            Target = target;
            FunctionIndices = functionIndices;
            return new(ImmutableDictionary<int, int>.Empty);
        }
    }
}
