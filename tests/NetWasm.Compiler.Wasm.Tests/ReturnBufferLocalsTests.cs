// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// A value-type local a method builds (initobj, then field stores through its address) and returns is kept in the
// caller's return buffer; one whose address reaches anything else, that is used before its initobj, or that a
// safepoint holds is not.
public sealed class ReturnBufferLocalsTests
{
    private static readonly CliTypeIdentity Pair = CliTypeIdentity.Named(new AssemblyIdentity("Program"), "", "Pair", isValueType: true);

    [Fact]
    public void ALocalBuiltAndReturnedLivesInTheBuffer()
    {
        var found = ReturnBufferLocals.Find(Method(Built()), Roots());

        Assert.Equal(new ReturnBufferLocal(0, 5, 6), found);
    }

    [Fact]
    public void ALocalIsNotKeptWhereItsAddressReachesACallOrItIsUsedBeforeItsInitobjOrASafepointHoldsIt()
    {
        ImmutableArray<CilInstruction> addressToCall =
        [
            I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)), I(1, CilOperation.InitializeObject),
            I(2, CilOperation.LoadLocalAddress, new CilOperand.Index(0)), I(3, CilOperation.Call), I(5, CilOperation.LoadLocal, new CilOperand.Index(0)),
        ];
        Assert.Null(ReturnBufferLocals.Find(Method(addressToCall, [[], [CliValueKind.ManagedAddress], [], [CliValueKind.ManagedAddress], []]), Roots()));

        ImmutableArray<CilInstruction> noInitobj =
        [
            I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)), I(1, CilOperation.LoadNull), I(2, CilOperation.StoreField),
            I(5, CilOperation.LoadLocal, new CilOperand.Index(0)),
        ];
        Assert.Null(ReturnBufferLocals.Find(Method(noInitobj, [[], [CliValueKind.ManagedAddress], [CliValueKind.ManagedAddress, CliValueKind.ManagedReference], []]), Roots()));

        var held = new MethodRootMap(EntryKey, ImmutableDictionary<RootSource, int>.Empty.Add(new(RootSourceKind.Local, 0, 0), 0),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(0, new(0, [new(RootSourceKind.Local, 0, 0)], [])));
        Assert.Null(ReturnBufferLocals.Find(Method(Built()), held));
    }

    // ldloca V; initobj; ldloca V; ldnull; stfld; ldloc V; ret.
    private static (ImmutableArray<CilInstruction>, ImmutableArray<ImmutableArray<CliValueKind>>) Built() =>
    (
        [
            I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)), I(1, CilOperation.InitializeObject),
            I(2, CilOperation.LoadLocalAddress, new CilOperand.Index(0)), I(3, CilOperation.LoadNull), I(4, CilOperation.StoreField),
            I(5, CilOperation.LoadLocal, new CilOperand.Index(0)),
        ],
        [[], [CliValueKind.ManagedAddress], [], [CliValueKind.ManagedAddress], [CliValueKind.ManagedAddress, CliValueKind.ManagedReference], []]
    );

    private static StructuredMethod Method((ImmutableArray<CilInstruction> Instructions, ImmutableArray<ImmutableArray<CliValueKind>> Stacks) body) =>
        Method(body.Instructions, body.Stacks);

    // One block of these instructions, each entered with the given stack, that returns a Pair (the return entered with one
    // value type).
    private static StructuredMethod Method(ImmutableArray<CilInstruction> instructions, ImmutableArray<ImmutableArray<CliValueKind>> stacks)
    {
        var ret = I(6, CilOperation.Return);
        var definition = new MethodDefinitionModel(EntryKey, TypeKey, "Make", true, new MethodSignatureModel(Pair, []), 1);
        var block = new StructuredBlockDefinition(new(0), 0, instructions, [], new StructuredTerminalExit(ret)) { EndOffset = 7 };
        var entries = instructions.Select((instruction, index) => (instruction.Offset, Stack: stacks[index]))
            .Append((ret.Offset, Stack: ImmutableArray.Create(CliValueKind.ValueType)))
            .ToImmutableDictionary(pair => pair.Offset, pair => pair.Stack);
        return new StructuredMethod(new(definition, null, 2, [CliValueKind.ValueType], [Pair], [.. instructions, ret]), new(0),
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty.Add(block.Id, block), StructuredSequence.Empty, [], [], [])
        {
            InstructionEntryStacks = entries,
        };
    }

    private static MethodRootMap Roots() => new(EntryKey, ImmutableDictionary<RootSource, int>.Empty, ImmutableDictionary<int, SafepointRootMap>.Empty);
}
