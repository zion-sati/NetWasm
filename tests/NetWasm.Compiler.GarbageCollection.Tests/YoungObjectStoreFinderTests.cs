// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection.Tests;

// The finder over in-memory bodies whose stated stacks it follows, or cannot: a store into a young this or object, or
// of null, needs no record; a stack the finder cannot follow records nothing it does not know.
public sealed class YoungObjectStoreFinderTests
{
    private const CliValueKind Reference = CliValueKind.ManagedReference;
    private const CliValueKind Integer = CliValueKind.I4;

    private static readonly AssemblyIdentity Assembly = new("YoungStores");
    private static readonly CliTypeIdentity Owner = CliTypeIdentity.Named(Assembly, "Tests", "Owner", false);
    private static readonly CilOperand Field = new CilOperand.Entity(new EntityKey(Assembly, 0x04000001));
    private static readonly CilOperand None = new CilOperand.None();

    // A young this read in the middle of a block: the store into it needs no record, unless the next instruction's
    // stated stack is deeper than the one followed, which the finder then forgets.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStoreIntoAYoungThisNeedsNoRecord(bool deeperStack)
    {
        // ldarg.0; ldnull; stfld; ret
        var body = Body([(CilOperation.LoadArgument, new CilOperand.Index(0)), (CilOperation.LoadNull, None),
            (CilOperation.StoreField, Field), (CilOperation.Return, None)],
            [[], deeperStack ? [Reference, Reference] : [Reference], [Reference, Reference], []], youngThis: true);

        Assert.Equal(!deeperStack, new YoungObjectStoreFinder().Find(body, ImmutableDictionary<int, SafepointRootMap>.Empty).Contains(2));
    }

    // A block no branch reaches does not start with a young this.
    [Fact]
    public void ABlockNoBranchReachesDoesNotStartWithAYoungThis()
    {
        // 0 ret; 1 ldarg.0; 2 ldc.i4 1; 3 stfld; 4 ret
        var body = Body([(CilOperation.Return, None), (CilOperation.LoadArgument, new CilOperand.Index(0)),
            (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), (CilOperation.StoreField, Field), (CilOperation.Return, None)],
            [[], [], [Reference], [Reference, Integer], []], youngThis: true);

        Assert.DoesNotContain(3, new YoungObjectStoreFinder().Find(body, ImmutableDictionary<int, SafepointRootMap>.Empty));
    }

    // A store whose operands the followed stack does not hold is not known to need no record.
    [Theory]
    [InlineData(CilOperation.StoreField)]
    [InlineData(CilOperation.StoreArrayElement)]
    [InlineData(CilOperation.StoreObject)]
    public void AStoreBeyondTheFollowedStackIsNotKnown(CilOperation store)
    {
        var body = Body([(store, Field), (CilOperation.Return, None)], [[], []], youngThis: false);

        Assert.Empty(new YoungObjectStoreFinder().Find(body, ImmutableDictionary<int, SafepointRootMap>.Empty));
    }

    // A newobj whose next instruction's stated stack is deeper than the followed one can pop: the followed stack starts
    // again at that depth, knowing nothing.
    [Fact]
    public void ANewObjectBeyondTheFollowedStackForgetsIt()
    {
        var constructor = new MethodInstanceModel(
            new MethodDefinitionModel(new EntityKey(Assembly, 0x06000002), new EntityKey(Assembly, 0x02000001), ".ctor", false,
                MethodSignatureModel.Create(CliValueKind.Void), 1), Owner, [], MethodSignatureModel.Create(CliValueKind.Void));
        // newobj; ldnull; stfld; ret, with newobj's next stack stated three deep
        var body = Body([(CilOperation.NewObject, new CilOperand.MethodInstance(constructor)), (CilOperation.LoadNull, None),
            (CilOperation.StoreField, Field), (CilOperation.Return, None)],
            [[], [Reference, Reference, Reference], [Reference, Reference, Reference, Reference], []], youngThis: false);

        // The store of null still needs no record; the object newobj made is not known to be young.
        Assert.Contains(2, new YoungObjectStoreFinder().Find(body, ImmutableDictionary<int, SafepointRootMap>.Empty));

        // newobj; nop; ret, with newobj's next stack stated empty: no room for its result.
        var shallow = Body([(CilOperation.NewObject, new CilOperand.MethodInstance(constructor)), (CilOperation.Nop, None),
            (CilOperation.Return, None)], [[], [], []], youngThis: false);
        Assert.Empty(new YoungObjectStoreFinder().Find(shallow, ImmutableDictionary<int, SafepointRootMap>.Empty));
    }

    private static ManagedMethodBody Body(
        (CilOperation Operation, CilOperand Operand)[] code, ImmutableArray<CliValueKind>[] stacks, bool youngThis)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        var definition = new MethodDefinitionModel(new EntityKey(Assembly, 0x06000001), new EntityKey(Assembly, 0x02000001),
            ".ctor", false, signature, 1);
        var instance = new MethodInstanceModel(definition, Owner, [], signature);
        var body = new CilMethodBody(definition, 4, [],
            [.. code.Select((instruction, offset) => new CilInstruction(offset, offset + 1, instruction.Operation, instruction.Operand))])
        {
            MethodInstance = instance,
            YoungThisAtEntry = youngThis,
        };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        return new ManagedMethodBody(instance, new ValidatedControlFlowGraph(graph,
            graph.Blocks.ToImmutableDictionary(block => block.Index, _ => ImmutableArray<CliValueKind>.Empty),
            stacks.Select((stack, offset) => (stack, offset)).ToImmutableDictionary(entry => entry.offset, entry => entry.stack)));
    }
}
