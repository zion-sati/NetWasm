// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis.ReceiverFlow;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ReachingLocalStoresTests
{
    [Fact]
    public void ALoadAfterAJoinReadsTheStoreOfEachArm()
    {
        var graph = Graph([
            (CilOperation.LoadArgument, new CilOperand.Index(0)),        // 0
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5)), // 1
            (CilOperation.LoadNull, new CilOperand.None()),              // 2
            (CilOperation.StoreLocal, new CilOperand.Index(0)),          // 3: one arm
            (CilOperation.Branch, new CilOperand.BranchTarget(7)),       // 4
            (CilOperation.LoadNull, new CilOperand.None()),              // 5
            (CilOperation.StoreLocal, new CilOperand.Index(0)),          // 6: the other
            (CilOperation.LoadLocal, new CilOperand.Index(0)),           // 7
            (CilOperation.Pop, new CilOperand.None()),
            (CilOperation.Return, new CilOperand.None()),
        ]);

        Assert.Equal([3, 6], Reaching(graph, 7));
    }

    [Fact]
    public void ALaterStoreInTheBlockHidesAnEarlierOne()
    {
        var graph = Graph([
            (CilOperation.LoadNull, new CilOperand.None()),
            (CilOperation.StoreLocal, new CilOperand.Index(0)),          // 1
            (CilOperation.LoadNull, new CilOperand.None()),
            (CilOperation.StoreLocal, new CilOperand.Index(0)),          // 3
            (CilOperation.LoadLocal, new CilOperand.Index(0)),           // 4
            (CilOperation.Pop, new CilOperand.None()),
            (CilOperation.Return, new CilOperand.None()),
        ]);

        Assert.Equal([3], Reaching(graph, 4));
    }

    [Fact]
    public void ALoadInALoopReadsTheStoreBeforeItAndTheOneInTheLoop()
    {
        var graph = Graph([
            (CilOperation.LoadNull, new CilOperand.None()),
            (CilOperation.StoreLocal, new CilOperand.Index(0)),          // 1: before the loop
            (CilOperation.LoadLocal, new CilOperand.Index(0)),           // 2: the loop's head
            (CilOperation.Pop, new CilOperand.None()),
            (CilOperation.LoadNull, new CilOperand.None()),
            (CilOperation.StoreLocal, new CilOperand.Index(0)),          // 5: in the loop
            (CilOperation.LoadArgument, new CilOperand.Index(0)),
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(2)), // 7
            (CilOperation.Return, new CilOperand.None()),
        ]);

        Assert.Equal([1, 5], Reaching(graph, 2));
    }

    [Fact]
    public void AHandlerReadsEveryStoreOfTheBlocksItProtects()
    {
        var graph = Graph([
            (CilOperation.LoadNull, new CilOperand.None()),              // 0: try
            (CilOperation.StoreLocal, new CilOperand.Index(0)),          // 1
            (CilOperation.LoadNull, new CilOperand.None()),
            (CilOperation.StoreLocal, new CilOperand.Index(0)),          // 3
            (CilOperation.LoadNull, new CilOperand.None()),
            (CilOperation.Throw, new CilOperand.None()),                 // 5
            (CilOperation.Pop, new CilOperand.None()),                   // 6: catch
            (CilOperation.LoadLocal, new CilOperand.Index(0)),           // 7
            (CilOperation.Pop, new CilOperand.None()),
            (CilOperation.Leave, new CilOperand.BranchTarget(10)),       // 9
            (CilOperation.Return, new CilOperand.None()),                // 10
        ], [new CilExceptionRegion(CilExceptionRegionKind.Catch, 0, 6, 6, 4,
            new EntityKey(ValidationTestData.Assembly, 0x02000002), null)]);

        Assert.Equal([1, 3], Reaching(graph, 7));
    }

    [Fact]
    public void AMethodTooLargeToFollowStoreByStoreIsLeftToItsSlots()
    {
        var operations = new List<(CilOperation, CilOperand)>();
        for (var index = 0; index < 4200; index++)
        {
            operations.Add((CilOperation.LoadNull, new CilOperand.None()));
            operations.Add((CilOperation.StoreLocal, new CilOperand.Index(0)));
            operations.Add((CilOperation.Nop, new CilOperand.None()));
            operations.Add((CilOperation.LoadArgument, new CilOperand.Index(0)));
            operations.Add((CilOperation.BranchIfTrue, new CilOperand.BranchTarget(operations.Count + 1)));
        }
        operations.Add((CilOperation.Return, new CilOperand.None()));

        Assert.Null(new ReachingLocalStores().Resolve(Graph(operations), new HashSet<int> { 0 }));
    }

    private static int[] Reaching(ControlFlowGraph graph, int load) =>
        [.. new ReachingLocalStores().Resolve(graph, new HashSet<int> { 0 })![load].Order()];

    private static ControlFlowGraph Graph(IReadOnlyList<(CilOperation Operation, CilOperand Operand)> operations,
        ImmutableArray<CilExceptionRegion> regions = default)
    {
        var definition = new MethodDefinitionModel(
            new(ValidationTestData.Assembly, 0x06000001),
            new(ValidationTestData.Assembly, 0x02000001), "Stores", true,
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4), 1);
        var body = new CilMethodBody(definition, 2, [CliValueKind.ManagedReference],
            [.. operations.Select((operation, offset) =>
                new CilInstruction(offset, offset + 1, operation.Operation, operation.Operand))])
        {
            ExceptionRegions = regions.IsDefault ? [] : regions,
        };
        return new ControlFlowGraphBuilderFactory().Create().Build(body);
    }
}
