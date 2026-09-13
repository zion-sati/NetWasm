using NetWasm.Compiler.ControlFlow.Draft;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using Final = NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ValidatedStructuredMethodBuilderTests
{
    [Fact]
    public void ConstructorRequiresEveryCapability()
    {
        IControlFlowStructuringStateBuilder states = new UnusedStateBuilder();
        IExceptionGroupStructurer exceptions = new UnusedExceptionGroupStructurer();
        IStructuredControlFlowBuilder flow = new UnusedStructuredControlFlowBuilder();
        IExceptionRegionOwnershipProjectorDraft exceptionRegionOwnership =
            new UnusedExceptionRegionOwnershipProjector();
        IStructuredControlFlowOwnershipResolverDraft ownership = new UnusedOwnershipResolver();
        IStructuredControlFlowValidatorDraft validator = new UnusedValidator();
        Final.IStructuredMethodCompleter completer = new UnusedCompleter();

        Assert.Throws<ArgumentNullException>(() =>
            new ValidatedStructuredMethodBuilder(
                null!, exceptions, flow, exceptionRegionOwnership, ownership, validator, completer));
        Assert.Throws<ArgumentNullException>(() =>
            new ValidatedStructuredMethodBuilder(
                states, null!, flow, exceptionRegionOwnership, ownership, validator, completer));
        Assert.Throws<ArgumentNullException>(() =>
            new ValidatedStructuredMethodBuilder(
                states, exceptions, null!, exceptionRegionOwnership, ownership, validator, completer));
        Assert.Throws<ArgumentNullException>(() =>
            new ValidatedStructuredMethodBuilder(
                states, exceptions, flow, null!, ownership, validator, completer));
        Assert.Throws<ArgumentNullException>(() =>
            new ValidatedStructuredMethodBuilder(
                states, exceptions, flow, exceptionRegionOwnership, null!, validator, completer));
        Assert.Throws<ArgumentNullException>(() =>
            new ValidatedStructuredMethodBuilder(
                states, exceptions, flow, exceptionRegionOwnership, ownership, null!, completer));
        Assert.Throws<ArgumentNullException>(() =>
            new ValidatedStructuredMethodBuilder(
                states, exceptions, flow, exceptionRegionOwnership, ownership, validator, null!));
    }

    private sealed class UnusedStateBuilder : IControlFlowStructuringStateBuilder
    {
        public ControlFlowStructuringState Build(ValidatedControlFlowGraph validated) =>
            throw new InvalidOperationException();
    }

    private sealed class UnusedExceptionGroupStructurer : IExceptionGroupStructurer
    {
        public ImmutableArray<StructuredExceptionGroupDraft> Structure(
            ControlFlowStructuringState state,
            ImmutableArray<CilExceptionRegion> regions) =>
            throw new InvalidOperationException();
    }

    private sealed class UnusedStructuredControlFlowBuilder : IStructuredControlFlowBuilder
    {
        public StructuredSequenceDraft Build(
            ControlFlowStructuringState state,
            int? start,
            int? stop,
            ImmutableHashSet<int> allowed,
            HashSet<int> path) =>
            throw new InvalidOperationException();

        public StructuredDispatcherDraft Build(
            ControlFlowStructuringState state,
            int? entry,
            ImmutableHashSet<int> component,
            ImmutableHashSet<int> allowed,
            int? exitStop,
            HashSet<int> path) =>
            throw new InvalidOperationException();
    }

    private sealed class UnusedExceptionRegionOwnershipProjector : IExceptionRegionOwnershipProjectorDraft
    {
        public StructuredMethodDraft Project(StructuredMethodDraft method) =>
            throw new InvalidOperationException();
    }

    private sealed class UnusedOwnershipResolver : IStructuredControlFlowOwnershipResolverDraft
    {
        public StructuredMethodDraft Resolve(StructuredMethodDraft method) => throw new InvalidOperationException();
    }

    private sealed class UnusedValidator : IStructuredControlFlowValidatorDraft
    {
        public void Validate(StructuredMethodDraft method) => throw new InvalidOperationException();
    }

    private sealed class UnusedCompleter : Final.IStructuredMethodCompleter
    {
        public Final.StructuredMethod Complete(StructuredMethodDraft draft) => throw new InvalidOperationException();
    }
}
