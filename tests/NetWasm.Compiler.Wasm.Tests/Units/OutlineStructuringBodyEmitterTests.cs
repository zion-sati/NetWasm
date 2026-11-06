using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests.Units;

// A method whose structuring was deferred is structured as it would have been before it is
// emitted; any other is emitted as it is.
public sealed class OutlineStructuringBodyEmitterTests
{
    private static readonly ManagedMethodBody Body = UnitActorFixture.Method();

    private static ManagedMethodBodyEmission Emit(OutlineStructuringBodyEmitter emitter, StructuredMethod structured) =>
        emitter.Emit(Body.Method.Definition, new(UnitActorFixture.Name(Body)), structured, UnitActorFixture.RootMap(Body), null!, null!);

    [Fact]
    public void AStructuredMethodIsEmittedAsItIs()
    {
        var inner = new Recording();
        var structured = UnitActorFixture.Structured(Body);

        Emit(new OutlineStructuringBodyEmitter(inner, new Lowerer(null!)), structured);

        Assert.Same(structured, inner.Emitted);
    }

    [Fact]
    public void AnOutlineIsStructuredWithItsPolicies()
    {
        var inner = new Recording();
        var structured = UnitActorFixture.Structured(Body);
        var lowerer = new Lowerer(structured);
        var outline = new StructuredMethodOutline(Body, BranchPrefixDuplicationPolicy.Speed, DispatcherEliminationPolicy.Disabled).StandIn([]);

        Emit(new OutlineStructuringBodyEmitter(inner, lowerer), outline);

        Assert.Same(structured, inner.Emitted);
        Assert.Equal((Body, BranchPrefixDuplicationPolicy.Speed, DispatcherEliminationPolicy.Disabled), lowerer.Lowered);
    }

    [Fact]
    public void AnOutlineWithoutTheCompilesStructuringCannotBeEmitted()
    {
        var inner = new Recording();
        var outline = new StructuredMethodOutline(Body, BranchPrefixDuplicationPolicy.Speed, DispatcherEliminationPolicy.Disabled).StandIn([]);

        Assert.Throws<InvalidOperationException>(() => Emit(new OutlineStructuringBodyEmitter(inner, null), outline));
        Assert.Null(inner.Emitted);
        Assert.Throws<ArgumentNullException>(() => new OutlineStructuringBodyEmitter(null!, null));
    }

    private sealed class Recording : IManagedMethodBodyEmitter
    {
        public StructuredMethod? Emitted { get; private set; }

        public ManagedMethodBodyEmission Emit(MethodDefinitionModel method, ManagedMethodIdentity callerIdentity, StructuredMethod structured,
            MethodRootMap rootMap, InstructionModuleTarget target, IFunctionIndexResolver functionIndices,
            MethodInstanceModel? methodInstance = null)
        {
            Emitted = structured;
            return new([0x00, 0x0B], 1, 0, null, "key", FilterEnvironmentLayout.Empty, ImmutableDictionary<int, int>.Empty);
        }
    }

    private sealed class Lowerer(StructuredMethod lowered) : IWasmMethodLowerer
    {
        public (ManagedMethodBody, BranchPrefixDuplicationPolicy, DispatcherEliminationPolicy)? Lowered { get; private set; }

        public StructuredMethod Lower(ManagedMethodBody method, BranchPrefixDuplicationPolicy branchPrefixDuplication,
            DispatcherEliminationPolicy dispatcherElimination)
        {
            Lowered = (method, branchPrefixDuplication, dispatcherElimination);
            return lowered;
        }
    }
}
