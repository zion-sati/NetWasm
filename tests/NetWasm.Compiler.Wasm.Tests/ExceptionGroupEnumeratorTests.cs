using System.Collections.Immutable;
using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ExceptionGroupEnumeratorTests
{
    [Fact]
    public void EnumeratesParentsBeforeNestedGroupsThroughItsContract()
    {
        var child = Group(1, []);
        var parent = Group(0, [new StructuredNestedExceptionGroup(child.Id)]);
        var method = Method([parent, child], [parent.Id]);

        Assert.Equal([parent.Id, child.Id], Create().Enumerate(method));
    }

    [Fact]
    public void EnumeratesEveryNestedStructuredSequenceLocationOnce()
    {
        var direct = Group(1, []);
        var whenTrue = Group(2, []);
        var whenFalse = Group(3, []);
        var loopBody = Group(4, []);
        var loopContinue = Group(5, []);
        var loopExit = Group(6, []);
        var postBody = Group(7, []);
        var postContinue = Group(8, []);
        var postExit = Group(9, []);
        var dispatcherExit = Group(10, []);
        var handler = Group(11, []);
        var filter = Group(12, []);
        var continuation = Group(13, []);
        var continuationDispatcherExit = Group(14, []);
        var sequence = new StructuredSequence(
        [
            new StructuredCode(Occurrence()),
            new StructuredExceptionRegion(direct.Id, new StructuredContinuationId(0)),
            new StructuredIf(
                Occurrence(),
                Sequence(whenTrue),
                Sequence(whenFalse)),
            new StructuredLoop(
                Occurrence(),
                true,
                Sequence(loopBody),
                Sequence(loopContinue),
                Sequence(loopExit)),
            new StructuredPostTestLoop(
                Sequence(postBody),
                Occurrence(),
                false,
                Sequence(postContinue),
                Sequence(postExit)),
            new StructuredDispatcher(
                null,
                [],
                [new StructuredDispatcherExit(
                    new StructuredBlockId(0),
                    Sequence(dispatcherExit))]),
        ]);
        var parent = Group(
            0,
            [new StructuredExceptionCode(sequence)],
            [
                Clause(CilExceptionRegionKind.Filter, Sequence(handler), Sequence(filter)),
                Clause(CilExceptionRegionKind.Catch, StructuredSequence.Empty, null),
            ],
            [new StructuredExceptionContinuation(
                new StructuredContinuationId(0),
                0,
                new StructuredBlockId(0),
                Sequence(continuation))],
            new StructuredDispatcher(
                null,
                [],
                [new StructuredDispatcherExit(
                    new StructuredBlockId(0),
                    Sequence(continuationDispatcherExit))]));

        var method = Method(
        [
            parent,
            direct,
            whenTrue,
            whenFalse,
            loopBody,
            loopContinue,
            loopExit,
            postBody,
            postContinue,
            postExit,
            dispatcherExit,
            handler,
            filter,
            continuation,
            continuationDispatcherExit,
        ],
        [parent.Id, direct.Id]);

        Assert.Equal(
            [
                parent.Id,
                direct.Id,
                whenTrue.Id,
                whenFalse.Id,
                loopBody.Id,
                loopContinue.Id,
                loopExit.Id,
                postBody.Id,
                postContinue.Id,
                postExit.Id,
                dispatcherExit.Id,
                handler.Id,
                filter.Id,
                continuation.Id,
                continuationDispatcherExit.Id,
            ],
            Create().Enumerate(method));
    }

    [Fact]
    public void RejectsMissingReferencedGroups()
    {
        var missing = new StructuredExceptionGroupId(0);
        var method = Method([], [missing]);

        Assert.Throws<InvalidOperationException>(() =>
            Create().Enumerate(method).ToArray());
    }

    private static ExceptionGroupEnumerator Create() =>
        new ExceptionGroupEnumerator();

    private static StructuredSequence Sequence(StructuredExceptionGroup group) =>
        new([new StructuredExceptionRegion(group.Id, null)]);

    private static StructuredBlockOccurrence Occurrence() =>
        new(new StructuredBlockId(0), StructuredBlockRole.Owner);

    private static StructuredExceptionClause Clause(
        CilExceptionRegionKind kind,
        StructuredSequence handler,
        StructuredSequence? filter) =>
        new(kind, 0, 1, null, filter is null ? null : 1, handler, filter)
        {
            HandlerBlock = new StructuredBlockId(0),
            FilterBlock = filter is null ? null : new StructuredBlockId(0),
        };

    private static StructuredExceptionGroup Group(
        int id,
        ImmutableArray<StructuredExceptionPart> parts,
        ImmutableArray<StructuredExceptionClause> clauses = default,
        ImmutableArray<StructuredExceptionContinuation> continuations = default,
        StructuredDispatcher? dispatcher = null) =>
        new(
            new StructuredExceptionGroupId(id),
            null,
            0,
            0,
            parts,
            clauses.IsDefault ? [] : clauses,
            continuations.IsDefault ? [] : continuations,
            dispatcher,
            null)
        {
            ProtectedBlocks = [],
        };

    private static StructuredMethod Method(
        IEnumerable<StructuredExceptionGroup> groups,
        ImmutableArray<StructuredExceptionGroupId> topLevel) =>
        new(
            new StructuredMethodHeader(
                new MethodDefinitionModel(
                    new EntityKey(new AssemblyIdentity("Tests"), 0x06000001),
                    new EntityKey(new AssemblyIdentity("Tests"), 0x02000001),
                    "Run",
                    true,
                    MethodSignatureModel.Create(CliValueKind.Void),
                    1),
                null,
                1,
                [],
                [],
                []),
            new StructuredBlockId(0),
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty,
            StructuredSequence.Empty,
            topLevel,
            groups.ToImmutableDictionary(group => group.Id),
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);

    private sealed record UnrelatedPart : StructuredExceptionPart;
}
