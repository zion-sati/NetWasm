// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Arrays;

namespace NetWasm.Compiler.Wasm.Tests;

// A run is the ldlens and element accesses of one slot's array in one block with no store to the slot between them:
// its first reads the length into the slot's local, the rest reuse it. A run of one is no run; a store, a slot a
// loop snapshot caches, a slot whose address is taken, an access proven in bounds, exception handlers and the
// switch being off each keep accesses out.
public sealed class ArrayLengthRunPlannerTests
{
    private static readonly CliTypeIdentity IntArray = CliTypeIdentity.SzArray(CliTypeIdentity.Primitive("i4", CliValueKind.I4));

    [Fact]
    public void AccessesOfOneSlotInABlockShareTheFirstOnesLength()
    {
        // local 0: element, length, element; local 1: one element; argument 0: two elements.
        var method = Method([.. Element(local: 0), .. Length(local: 0), .. Element(local: 1), .. Element(local: 0),
            .. Element(argument: 0), .. Element(argument: 0)]);

        var plan = new ArrayLengthRunPlanner(new Arguments()).Plan(method, ArrayMetadataCachingPlan.Empty, true);

        Assert.Equal(new Dictionary<int, ArrayReferenceSlot> { [2] = new(false, 0), [17] = new(true, 0) }, plan.Loads);
        Assert.Equal(new Dictionary<int, ArrayReferenceSlot> { [5] = new(false, 0), [13] = new(false, 0), [21] = new(true, 0) },
            plan.Reuses);
    }

    [Fact]
    public void AStoreToTheSlotEndsTheRun()
    {
        var method = Method([.. Element(local: 0), .. Element(local: 0), .. Store(local: 0), .. Element(local: 0)]);

        var plan = new ArrayLengthRunPlanner(new Arguments()).Plan(method, ArrayMetadataCachingPlan.Empty, true);

        Assert.Equal([2], plan.Loads.Keys);
        Assert.Equal([6], plan.Reuses.Keys);
    }

    [Fact]
    public void SlotsALoopCachesAndSlotsWithTheirAddressTakenAreLeftAlone()
    {
        var method = Method([.. Element(local: 0), .. Element(local: 0), .. Element(local: 1), .. Element(local: 1),
            new(CilOperation.LoadLocalAddress, new CilOperand.Index(1), []), new(CilOperation.Pop, new CilOperand.None(), [CliValueKind.ManagedAddress])]);
        var cached = ArrayMetadataCachingPlan.Empty with { Slots = [new(false, 0)] };

        var plan = new ArrayLengthRunPlanner(new Arguments()).Plan(method, cached, true);

        Assert.Same(ArrayLengthRunPlan.Empty, plan);
    }

    [Fact]
    public void AnAccessProvenInBoundsReadsNoLengthAndStaysOut()
    {
        var method = Method([.. Element(local: 0), .. Element(local: 0), .. Element(local: 0)]);
        method = method with
        {
            Header = method.Header with
            {
                ArrayBoundsProof = new(method.Header.Instructions, method.Header.Locals, method.Header.LocalSignatureTypes, [2]),
            },
        };

        var plan = new ArrayLengthRunPlanner(new Arguments()).Plan(method, ArrayMetadataCachingPlan.Empty, true);

        Assert.Equal([6], plan.Loads.Keys);
        Assert.Equal([10], plan.Reuses.Keys);
    }

    [Fact]
    public void HandlersOrTheSwitchOffPlanNothing()
    {
        var method = Method([.. Element(local: 0), .. Element(local: 0)]);
        var planner = new ArrayLengthRunPlanner(new Arguments());

        Assert.Same(ArrayLengthRunPlan.Empty, planner.Plan(method, ArrayMetadataCachingPlan.Empty, false));
        Assert.Same(ArrayLengthRunPlan.Empty, planner.Plan(method with
        {
            ExceptionGroups = ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty
                .Add(new(0), null!),
        }, ArrayMetadataCachingPlan.Empty, true));
        Assert.Throws<ArgumentNullException>(() => new ArrayLengthRunPlanner(null!));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!, ArrayMetadataCachingPlan.Empty, true));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(method, null!, true));
    }

    private sealed record Step(CilOperation Operation, CilOperand Operand, ImmutableArray<CliValueKind> Stack);

    // `ldloc|ldarg; ldc.i4.0; ldelem.i4; pop`, the access at the third offset.
    private static Step[] Element(int? local = null, int? argument = null) =>
    [
        new(local is null ? CilOperation.LoadArgument : CilOperation.LoadLocal, new CilOperand.Index(local ?? argument!.Value), []),
        new(CilOperation.LoadInt32, new CilOperand.ConstantI4(0), [CliValueKind.ManagedReference]),
        new(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(CliTypeIdentity.Primitive("i4", CliValueKind.I4)),
            [CliValueKind.ManagedReference, CliValueKind.I4]),
        new(CilOperation.Pop, new CilOperand.None(), [CliValueKind.I4]),
    ];

    // `ldloc; ldlen; pop`, the access at the second offset.
    private static Step[] Length(int local) =>
    [
        new(CilOperation.LoadLocal, new CilOperand.Index(local), []),
        new(CilOperation.LoadArrayLength, new CilOperand.None(), [CliValueKind.ManagedReference]),
        new(CilOperation.Pop, new CilOperand.None(), [CliValueKind.NativeInt]),
    ];

    private static Step[] Store(int local) =>
    [
        new(CilOperation.LoadNull, new CilOperand.None(), []),
        new(CilOperation.StoreLocal, new CilOperand.Index(local), [CliValueKind.ManagedReference]),
    ];

    private static StructuredMethod Method(Step[] steps)
    {
        var assembly = new AssemblyIdentity("array-length-run-tests");
        var definition = new MethodDefinitionModel(new(assembly, 0x06000001), new(assembly, 0x02000001),
            "Body", true, MethodSignatureModel.Create(CliValueKind.Void) with
            {
                ParameterTypes = [CliValueKind.ManagedReference],
                ParameterSignatureTypes = [IntArray],
            }, 1);
        var instructions = steps.Select((step, offset) => new CilInstruction(offset, offset + 1, step.Operation, step.Operand))
            .Append(new CilInstruction(steps.Length, steps.Length + 1, CilOperation.Return, new CilOperand.None()))
            .ToImmutableArray();
        var stacks = steps.Select((step, offset) => (offset, step.Stack))
            .Append((steps.Length, ImmutableArray<CliValueKind>.Empty))
            .ToImmutableDictionary(entry => entry.Item1, entry => entry.Item2);
        return new(new(definition, null, 4, [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
                [IntArray, IntArray], instructions), new(0),
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
                .Add(new(0), new(new(0), 0, instructions, [], new StructuredFallthroughExit(null)) { EndOffset = instructions.Length }),
            new([new StructuredCode(new(new(0), StructuredBlockRole.Owner))]), [], [], stacks);
    }

    private sealed class Arguments : IArgumentSignatureTypeResolver
    {
        public CliTypeIdentity Resolve(StructuredMethodHeader header, int index) =>
            header.Method.Signature.ParameterSignatureTypes[index];
    }
}
