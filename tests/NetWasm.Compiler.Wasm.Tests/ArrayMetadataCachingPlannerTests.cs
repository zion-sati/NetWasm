using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Arrays;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ArrayMetadataCachingPlannerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReassignedLoopStorageCannotSupplyCachedMetadata(bool argument)
    {
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadNull, new CilOperand.None()),
            Step(argument ? CilOperation.StoreArgument : CilOperation.StoreLocal,
                new CilOperand.Index(0), CliValueKind.ManagedReference));
        var load = method.Header.Instructions[0] with
        {
            Operation = argument ? CilOperation.LoadArgument : CilOperation.LoadLocal,
        };
        method = method with
        {
            Header = method.Header with
            {
                Method = method.Header.Method with
                {
                    Signature = MethodSignatureModel.Create(CliValueKind.I4) with
                    {
                        ParameterTypes = [CliValueKind.ManagedReference],
                        ParameterSignatureTypes = [method.Header.LocalSignatureTypes[0]],
                    },
                },
                Instructions = method.Header.Instructions.SetItem(0, load),
            },
            Blocks = method.Blocks.SetItem(new(0), method.Blocks[new(0)] with
            {
                Instructions = method.Blocks[new(0)].Instructions.SetItem(0, load),
            }),
            Body = new([new StructuredLoop(new(new(0), StructuredBlockRole.Owner), true,
                new([new StructuredCode(new(new(1), StructuredBlockRole.Owner))]),
                StructuredSequence.Empty, StructuredSequence.Empty)]),
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Same(ArrayMetadataCachingPlan.Empty, plan);
    }

    // Arrays of rank two or more are cached with their rank, for every
    // element load and store, whatever the number of indexes.
    [Fact]
    public void RectangularArraysRecordTheirRankAndEveryAccess()
    {
        var assembly = new AssemblyIdentity("array-metadata-tests");
        var definition = new MethodDefinitionModel(new(assembly, 0x06000001), new(assembly, 0x02000001),
            "Body", true, MethodSignatureModel.Create(CliValueKind.I4), 1);
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var (grid, cube) = (CliTypeIdentity.Array(scalar, 2), CliTypeIdentity.Array(scalar, 3));
        var steps = new[]
        {
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(0), CliValueKind.ManagedReference),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(1), CliValueKind.ManagedReference, CliValueKind.I4),
            Step(CilOperation.LoadRectangularArrayElement, new CilOperand.TypeIdentity(grid),
                CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4),
            Step(CilOperation.LoadLocal, new CilOperand.Index(1), CliValueKind.I4),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(0), CliValueKind.I4, CliValueKind.ManagedReference),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(1), CliValueKind.I4, CliValueKind.ManagedReference,
                CliValueKind.I4),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(2), CliValueKind.I4, CliValueKind.ManagedReference,
                CliValueKind.I4, CliValueKind.I4),
            Step(CilOperation.LoadRectangularArrayElementAddress, new CilOperand.TypeIdentity(cube), CliValueKind.I4,
                CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4, CliValueKind.I4),
            Step(CilOperation.Pop, new CilOperand.None(), CliValueKind.I4, CliValueKind.ManagedAddress),
            Step(CilOperation.StoreLocal, new CilOperand.Index(2), CliValueKind.I4),
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(1), CliValueKind.ManagedReference),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(0), CliValueKind.ManagedReference, CliValueKind.I4),
            Step(CilOperation.LoadLocal, new CilOperand.Index(2), CliValueKind.ManagedReference, CliValueKind.I4,
                CliValueKind.I4),
            Step(CilOperation.StoreRectangularArrayElement, new CilOperand.TypeIdentity(grid),
                CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4, CliValueKind.I4),
        };
        var instructions = steps.Select((step, index) =>
            new CilInstruction(index, index + 1, step.Operation, step.Operand)).ToImmutableArray();
        var method = new StructuredMethod(
            new(definition, null, 6, [CliValueKind.ManagedReference, CliValueKind.ManagedReference, CliValueKind.I4],
                [grid, cube, scalar], instructions),
            new(0),
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
                .Add(new(0), new(new(0), 0, instructions, [], new StructuredFallthroughExit(null)) { EndOffset = steps.Length }),
            new([new StructuredLoop(new(new(0), StructuredBlockRole.Owner), true,
                StructuredSequence.Empty, StructuredSequence.Empty, StructuredSequence.Empty)]),
            [], [],
            steps.Select((step, index) => KeyValuePair.Create(index, step.Stack)).ToImmutableDictionary());

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        var (gridSlot, cubeSlot) = (new ArrayReferenceSlot(false, 0), new ArrayReferenceSlot(false, 1));
        Assert.Equal([gridSlot, cubeSlot], plan.Slots.ToArray());
        Assert.Equal(2, plan.Ranks[gridSlot]);
        Assert.Equal(3, plan.Ranks[cubeSlot]);
        Assert.Equal([(3, gridSlot), (8, cubeSlot), (15, gridSlot)],
            plan.Accesses.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)));
    }

    [Fact]
    public void AssignmentDoesNotTurnAnUnknownStackReferenceIntoACachedOrigin()
    {
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadNull, new CilOperand.None()),
            Step(CilOperation.LoadLocal, new CilOperand.Index(0), CliValueKind.ManagedReference),
            Step(CilOperation.StoreLocal, new CilOperand.Index(0),
                CliValueKind.ManagedReference, CliValueKind.ManagedReference),
            Step(CilOperation.LoadArrayLength, new CilOperand.None(), CliValueKind.ManagedReference));

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.DoesNotContain(23, plan.Accesses.Keys);
        Assert.Equal([new ArrayReferenceSlot(false, 0)], plan.Slots.ToArray());
    }

    [Fact]
    public void BinaryIndexArithmeticPreservesTheArrayBelowItsOperands()
    {
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(0), CliValueKind.ManagedReference),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(1), CliValueKind.ManagedReference, CliValueKind.I4),
            Step(CilOperation.Add, new CilOperand.None(), CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4),
            Step(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                CliValueKind.ManagedReference, CliValueKind.I4));

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal(new ArrayReferenceSlot(false, 0), plan.Accesses[24]);
    }

    [Fact]
    public void UnknownCallEffectsDoNotRecoverOriginsFromStackDepth()
    {
        var assembly = new AssemblyIdentity("array-metadata-tests");
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        var definition = new MethodDefinitionModel(new(assembly, 0x06000002), new(assembly, 0x02000001),
            "Other", true, signature, 1);
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.Call, new CilOperand.MethodInstance(new(definition,
                CliTypeIdentity.Named(assembly, "Tests", "Owner", false), [], signature)), CliValueKind.ManagedReference),
            Step(CilOperation.LoadArrayLength, new CilOperand.None(), CliValueKind.ManagedReference));

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.DoesNotContain(22, plan.Accesses.Keys);
        Assert.Equal([new ArrayReferenceSlot(false, 0)], plan.Slots.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjectAndFieldStoresPreserveTheArrayBelowTheirOperands(bool fieldStore)
    {
        var scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var assembly = new AssemblyIdentity("array-metadata-tests");
        var cell = fieldStore ? CliTypeIdentity.Named(assembly, "Tests", "Cell", true) : scalar;
        CilOperand operand = fieldStore
            ? new CilOperand.FieldInstance(new(new(new(assembly, 0x04000001), new(assembly, 0x02000002),
                "Value", scalar, false), cell, scalar))
            : new CilOperand.TypeIdentity(scalar);
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.LoadLocalAddress, new CilOperand.Index(2), CliValueKind.ManagedReference),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(7), CliValueKind.ManagedReference, CliValueKind.ManagedAddress),
            Step(fieldStore ? CilOperation.StoreField : CilOperation.StoreObject, operand,
                CliValueKind.ManagedReference, CliValueKind.ManagedAddress, CliValueKind.I4),
            Step(CilOperation.LoadArrayLength, new CilOperand.None(), CliValueKind.ManagedReference));
        method = method with
        {
            Header = method.Header with
            {
                Locals = method.Header.Locals.Add(cell.StackKind),
                LocalSignatureTypes = method.Header.LocalSignatureTypes.Add(cell),
            },
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal(new ArrayReferenceSlot(false, 0), plan.Accesses[24]);
    }

    [Theory]
    [InlineData(CilOperation.LoadArrayElement, CliValueKind.I4)]
    [InlineData(CilOperation.LoadArrayElementReference, CliValueKind.ManagedReference)]
    [InlineData(CilOperation.LoadArrayElementAddress, CliValueKind.ManagedAddress)]
    public void ElementLoadsReuseTheKnownArraySnapshot(CilOperation operation, CliValueKind result)
    {
        var element = CliTypeIdentity.FromStackKind(operation == CilOperation.LoadArrayElementReference
            ? CliValueKind.ManagedReference : CliValueKind.I4);
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(0), CliValueKind.ManagedReference),
            Step(operation, new CilOperand.TypeIdentity(element),
                CliValueKind.ManagedReference, CliValueKind.I4),
            Step(CilOperation.Pop, new CilOperand.None(), result));
        method = method with
        {
            Header = method.Header with
            {
                LocalSignatureTypes = method.Header.LocalSignatureTypes.SetItem(0, CliTypeIdentity.SzArray(element)),
            },
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal(new ArrayReferenceSlot(false, 0), plan.Accesses[22]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ElementStoresLocateTheArrayBelowIndexAndValue(bool reference)
    {
        var value = reference ? CliValueKind.ManagedReference : CliValueKind.I4;
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(0), CliValueKind.ManagedReference),
            Step(reference ? CilOperation.LoadNull : CilOperation.LoadInt32,
                reference ? new CilOperand.None() : new CilOperand.ConstantI4(7),
                CliValueKind.ManagedReference, CliValueKind.I4),
            Step(reference ? CilOperation.StoreArrayElementReference : CilOperation.StoreArrayElement,
                new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(value)),
                CliValueKind.ManagedReference, CliValueKind.I4, value));
        method = method with
        {
            Header = method.Header with
            {
                LocalSignatureTypes = method.Header.LocalSignatureTypes.SetItem(0,
                    CliTypeIdentity.SzArray(CliTypeIdentity.FromStackKind(value))),
            },
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal(new ArrayReferenceSlot(false, 0), plan.Accesses[23]);
    }

    [Theory]
    [InlineData(CilOperation.Nop)]
    [InlineData(CilOperation.Readonly)]
    [InlineData(CilOperation.Volatile)]
    [InlineData(CilOperation.Unaligned)]
    public void StackNeutralOperationsPreserveKnownArrayOrigins(CilOperation operation)
    {
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(operation, new CilOperand.None(), CliValueKind.ManagedReference),
            Step(CilOperation.LoadArrayLength, new CilOperand.None(), CliValueKind.ManagedReference));

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal(new ArrayReferenceSlot(false, 0), plan.Accesses[22]);
    }

    [Theory]
    [InlineData(CilOperation.Negate)]
    [InlineData(CilOperation.OnesComplement)]
    [InlineData(CilOperation.ConvertInt32)]
    public void IndexTransformationsDoNotEraseTheArrayBelowThem(CilOperation operation)
    {
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(0), CliValueKind.ManagedReference),
            Step(operation, new CilOperand.None(), CliValueKind.ManagedReference, CliValueKind.I4),
            Step(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                CliValueKind.ManagedReference, CliValueKind.I4));

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal(new ArrayReferenceSlot(false, 0), plan.Accesses[23]);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void StoreInvalidatesOnlyStackOriginsOfTheAssignedLocal(int assignedLocal, bool retained)
    {
        var method = WithOutsideInstructions(
            Step(CilOperation.LoadLocal, new CilOperand.Index(0)),
            Step(CilOperation.Duplicate, new CilOperand.None(), CliValueKind.ManagedReference),
            Step(CilOperation.StoreLocal, new CilOperand.Index(assignedLocal),
                CliValueKind.ManagedReference, CliValueKind.ManagedReference),
            Step(CilOperation.LoadArrayLength, new CilOperand.None(), CliValueKind.ManagedReference));

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal(retained, plan.Accesses.ContainsKey(23));
        Assert.Equal([new ArrayReferenceSlot(false, 0)], plan.Slots.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArrayArgumentsRequireUnexposedReferenceStorage(bool exposed)
    {
        var method = Create(false);
        var load = method.Header.Instructions[0] with { Operation = CilOperation.LoadArgument };
        var instructions = method.Header.Instructions.SetItem(0, load);
        if (exposed)
            instructions = instructions.Add(new(20, 21, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)));
        method = method with
        {
            Header = method.Header with
            {
                Method = method.Header.Method with
                {
                    Signature = MethodSignatureModel.Create(CliValueKind.I4) with
                    {
                        ParameterTypes = [CliValueKind.ManagedReference],
                        ParameterSignatureTypes = [method.Header.LocalSignatureTypes[0]],
                    },
                },
                Instructions = instructions,
            },
            Blocks = method.Blocks.SetItem(new(0), method.Blocks[new(0)] with
            {
                Instructions = method.Blocks[new(0)].Instructions.SetItem(0, load),
            }),
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        if (exposed)
            Assert.Same(ArrayMetadataCachingPlan.Empty, plan);
        else
            Assert.Equal([new ArrayReferenceSlot(true, 0)], plan.Slots.ToArray());
    }

    [Fact]
    public void LoopsInsideConditionalsRetainTheirSnapshotRequirements()
    {
        var method = Create(false);
        method = method with
        {
            Body = new([new StructuredIf(new(new(0), StructuredBlockRole.ExecutingReplica),
                method.Body, StructuredSequence.Empty)]),
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal([new ArrayReferenceSlot(false, 0)], plan.Slots.ToArray());
        Assert.Equal(plan.Slots.ToArray(), plan.LoopEntries[new(0)].ToArray());
    }

    [Fact]
    public void DispatcherBlocksAreRepeatingButTheirExitIsNot()
    {
        var method = Create(false);
        method = method with
        {
            Body = new([new StructuredDispatcher(new(0),
                [new(new(new(0), StructuredBlockRole.Owner), null, null)],
                [new(new(1), new([new StructuredCode(new(new(1), StructuredBlockRole.Owner))]))])]),
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal([new ArrayReferenceSlot(false, 0)], plan.Slots.ToArray());
        Assert.Empty(plan.LoopEntries);
    }

    [Fact]
    public void UnconditionalLoopBodiesRepeatWithoutALoopEntry()
    {
        var method = Create(false);
        method = method with
        {
            Body = new([new StructuredLoop(null, true,
                new([new StructuredCode(new(new(0), StructuredBlockRole.Owner))]),
                StructuredSequence.Empty, StructuredSequence.Empty)]),
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        // Like a dispatcher, the loop has no entry condition to snapshot before.
        Assert.Equal([new ArrayReferenceSlot(false, 0)], plan.Slots.ToArray());
        Assert.Empty(plan.LoopEntries);
    }

    [Fact]
    public void RejectsMissingResolverAndMethod()
    {
        Assert.Throws<ArgumentNullException>(() => new ArrayMetadataCachingPlanner(null!));
        Assert.Throws<ArgumentNullException>(() => new ArrayMetadataCachingPlanner(new Arguments()).Plan(null!, true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MissingStackFactsCannotEstablishALoopSnapshot(int offset)
    {
        var method = Create(false);
        method = method with { InstructionEntryStacks = method.InstructionEntryStacks.Remove(offset) };

        Assert.Same(ArrayMetadataCachingPlan.Empty,
            new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true));
    }

    [Fact]
    public void RepeatedLoopConditionUnionsTheAccessesOfBothBodies()
    {
        var method = Create(false);
        var second = new StructuredLoop(new(new(0), StructuredBlockRole.ExecutingReplica), true,
            new([new StructuredCode(new(new(1), StructuredBlockRole.ExecutingReplica))]),
            StructuredSequence.Empty, StructuredSequence.Empty);
        method = method with { Body = new([method.Body.Regions[0], second]) };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal([new ArrayReferenceSlot(false, 0), new ArrayReferenceSlot(false, 1)], plan.Slots.ToArray());
        Assert.Equal(plan.Slots.ToArray(), plan.LoopEntries[new(0)].ToArray());
    }

    [Fact]
    public void NonrepeatingCodeDoesNotCreateSnapshots()
    {
        var method = Create(false);
        method = method with
        {
            Body = new([new StructuredCode(new(new(0), StructuredBlockRole.Owner)),
                new StructuredCode(new(new(1), StructuredBlockRole.Owner))]),
        };

        Assert.Same(ArrayMetadataCachingPlan.Empty,
            new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectsOnlyLoopUsedSlotsAtEntry(bool postTest)
    {
        var method = Create(postTest);
        var planner = new[] { new ArrayMetadataCachingPlanner(new Arguments()) }
            .Cast<IArrayMetadataCachingPlanner>().Single();

        var plan = planner.Plan(method, true);

        Assert.Equal([new ArrayReferenceSlot(false, 0)], plan.Slots.ToArray());
        Assert.Equal(plan.Slots.ToArray(), plan.LoopEntries[new(0)].ToArray());
        Assert.DoesNotContain(new ArrayReferenceSlot(false, 1), plan.Slots);
    }

    [Fact]
    public void ExposedReferenceHasNoSnapshots()
    {
        var method = Create(false);
        method = method with
        {
            Header = method.Header with
            {
                Instructions = method.Header.Instructions.Add(new(20, 21,
                CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
            }
        };

        Assert.Same(ArrayMetadataCachingPlan.Empty,
            new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true));
    }

    [Fact]
    public void LoopsInsideAnotherLoopUseItsEntrySnapshot()
    {
        var method = Create(false);
        var inner = new StructuredLoop(new(new(0), StructuredBlockRole.Owner), true,
            StructuredSequence.Empty, StructuredSequence.Empty, StructuredSequence.Empty);
        method = method with
        {
            Body = new([new StructuredLoop(new(new(1), StructuredBlockRole.Owner), true,
                new([inner]), StructuredSequence.Empty, StructuredSequence.Empty)]),
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true, true);

        // Neither slot is stored inside the outer loop, so its entry snapshot
        // holds for the inner one, which still checks its condition's array.
        Assert.Equal([new ArrayReferenceSlot(false, 0), new ArrayReferenceSlot(false, 1)], plan.Slots.ToArray());
        Assert.Equal(plan.Slots.ToArray(), plan.LoopEntries[new(1)].ToArray());
        Assert.False(plan.LoopEntries.ContainsKey(new(0)));
        Assert.Equal([new ArrayReferenceSlot(false, 0)], plan.EntryNullChecks[new(0)].ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnArgumentNeverStoredKeepsItsMethodEntrySnapshot(bool stored)
    {
        var method = WithOutsideInstructions(stored
            ? [Step(CilOperation.LoadNull, new CilOperand.None()),
                Step(CilOperation.StoreArgument, new CilOperand.Index(0), CliValueKind.ManagedReference)]
            : [Step(CilOperation.Nop, new CilOperand.None())]);
        var load = method.Header.Instructions[0] with { Operation = CilOperation.LoadArgument };
        method = method with
        {
            Header = method.Header with
            {
                Method = method.Header.Method with
                {
                    Signature = MethodSignatureModel.Create(CliValueKind.I4) with
                    {
                        ParameterTypes = [CliValueKind.ManagedReference],
                        ParameterSignatureTypes = [method.Header.LocalSignatureTypes[0]],
                    },
                },
                Instructions = method.Header.Instructions.SetItem(0, load),
            },
            Blocks = method.Blocks.SetItem(new(0), method.Blocks[new(0)] with
            {
                Instructions = method.Blocks[new(0)].Instructions.SetItem(0, load),
            }),
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true);

        Assert.Equal([new ArrayReferenceSlot(true, 0)], plan.Slots.ToArray());
        Assert.Equal(stored ? plan.Slots.ToArray() : [], plan.LoopEntries[new(0)].ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEntryNullCheckAnEarlierAccessProvesIsNotEmitted(bool stale)
    {
        var method = Create(false);
        var header = method.Header;
        method = method with
        {
            Header = header with
            {
                ArrayBoundsProof = new(stale ? [.. header.Instructions] : header.Instructions, header.Locals,
                    header.LocalSignatureTypes, [])
                { NonNullOffsets = [1] },
            },
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(method, true, true);

        Assert.Equal(stale, plan.EntryNullChecks.ContainsKey(new(0)));
        Assert.Equal([1], plan.CheckedAccesses.ToArray());
    }

    [Fact]
    public void DisabledPolicyHasNoEntryRefreshes() => Assert.Empty(
        new ArrayMetadataCachingPlanner(new Arguments()).Plan(Create(false), false).LoopEntries);

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void APreTestConditionsFirstNullCheckMovesToTheLoopEntry(bool postTest, bool hoist)
    {
        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(Create(postTest), true, hoist);

        var hoisted = hoist && !postTest;
        Assert.Equal(hoisted, plan.EntryNullChecks.TryGetValue(new(0), out var checks));
        if (hoisted) Assert.Equal([new ArrayReferenceSlot(false, 0)], checks.ToArray());
        Assert.Equal(hoisted ? [1] : [], plan.CheckedAccesses.ToArray());
    }

    [Theory]
    [InlineData("lengths", new[] { 0, 1 })]
    [InlineData("repeated", new[] { 0 })]
    [InlineData("element", new[] { 0 })]
    [InlineData("effect", new int[0])]
    [InlineData("unknown", new int[0])]
    public void OnlyChecksAheadOfAnyOtherEffectMoveToTheLoopEntry(string shape, int[] slots)
    {
        var none = new CilOperand.None();
        var reference = CliValueKind.ManagedReference;
        InstructionStep Load(int local, params CliValueKind[] stack) =>
            Step(CilOperation.LoadLocal, new CilOperand.Index(local), stack);
        InstructionStep[] steps = shape switch
        {
            // A length read has no other effect, so the next check moves too.
            "lengths" =>
            [
                Load(0), Step(CilOperation.LoadArrayLength, none, reference), Step(CilOperation.Pop, none, CliValueKind.I4),
                Load(1), Step(CilOperation.LoadArrayLength, none, reference),
            ],
            "repeated" =>
            [
                Load(0), Step(CilOperation.LoadArrayLength, none, reference), Step(CilOperation.Pop, none, CliValueKind.I4),
                Load(0), Step(CilOperation.LoadArrayLength, none, reference),
            ],
            // An element read may throw for its index before the next check.
            "element" =>
            [
                Load(0), Step(CilOperation.LoadInt32, new CilOperand.ConstantI4(0), reference),
                Step(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                    reference, CliValueKind.I4),
                Step(CilOperation.Pop, none, CliValueKind.I4), Load(1), Step(CilOperation.LoadArrayLength, none, reference),
            ],
            "effect" =>
            [
                Step(CilOperation.LoadStaticField, new CilOperand.Entity(new(new("array-metadata-tests"), 0x04000001))),
                Step(CilOperation.Pop, none, CliValueKind.I4), Load(0), Step(CilOperation.LoadArrayLength, none, reference),
            ],
            _ =>
            [
                Step(CilOperation.LoadNull, none), Step(CilOperation.LoadArrayLength, none, reference),
                Step(CilOperation.Pop, none, CliValueKind.I4), Load(0), Step(CilOperation.LoadArrayLength, none, reference),
            ],
        };

        var plan = new ArrayMetadataCachingPlanner(new Arguments()).Plan(Condition(steps), true, hoistNullChecks: true);

        Assert.Equal(slots.Select(slot => new ArrayReferenceSlot(false, slot)),
            plan.EntryNullChecks.TryGetValue(new(0), out var checks) ? checks.ToArray() : []);
    }

    private static InstructionStep Step(CilOperation operation, CilOperand operand,
        params CliValueKind[] stack) => new(operation, operand, [.. stack]);

    private readonly record struct InstructionStep(CilOperation Operation, CilOperand Operand,
        ImmutableArray<CliValueKind> Stack);

    private static StructuredMethod WithOutsideInstructions(params InstructionStep[] steps)
    {
        var method = Create(false);
        var instructions = steps.Select((step, index) =>
            new CilInstruction(20 + index, 21 + index, step.Operation, step.Operand)).ToImmutableArray();
        return method with
        {
            Header = method.Header with
            {
                MaxStack = 8,
                Instructions = [.. method.Blocks[new(0)].Instructions, .. instructions],
            },
            Blocks = method.Blocks.SetItem(new(1), method.Blocks[new(1)] with
            {
                StartOffset = 20,
                EndOffset = 20 + steps.Length,
                Instructions = instructions,
            }),
            InstructionEntryStacks = method.InstructionEntryStacks.Remove(2).Remove(3)
                .AddRange(steps.Select((step, index) => new KeyValuePair<int, ImmutableArray<CliValueKind>>(20 + index, step.Stack))),
        };
    }

    // A pre-test loop whose condition block runs the steps.
    private static StructuredMethod Condition(params InstructionStep[] steps)
    {
        var method = Create(false);
        var instructions = steps.Select((step, index) =>
            new CilInstruction(index, index + 1, step.Operation, step.Operand)).ToImmutableArray();
        var exit = method.Blocks[new(1)].Instructions.Select(instruction => instruction with
        {
            Offset = instruction.Offset + 100,
            NextOffset = instruction.NextOffset + 100,
        }).ToImmutableArray();
        return method with
        {
            Header = method.Header with { MaxStack = 8, Instructions = [.. instructions, .. exit] },
            Blocks = method.Blocks
                .SetItem(new(0), method.Blocks[new(0)] with { EndOffset = steps.Length, Instructions = instructions })
                .SetItem(new(1), method.Blocks[new(1)] with { StartOffset = 102, EndOffset = 104, Instructions = exit }),
            InstructionEntryStacks = steps.Select((step, index) => (step, index))
                .ToImmutableDictionary(pair => pair.index, pair => pair.step.Stack)
                .Add(102, []).Add(103, [CliValueKind.ManagedReference]),
        };
    }

    private static StructuredMethod Create(bool postTest)
    {
        var assembly = new AssemblyIdentity("array-metadata-tests");
        var definition = new MethodDefinitionModel(new(assembly, 0x06000001), new(assembly, 0x02000001),
            "Body", true, MethodSignatureModel.Create(CliValueKind.I4), 1);
        var array = CliTypeIdentity.SzArray(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        ImmutableArray<CilInstruction> instructions =
        [
            new(0, 1, CilOperation.LoadLocal, new CilOperand.Index(0)),
            new(1, 2, CilOperation.LoadArrayLength, new CilOperand.None()),
            new(2, 3, CilOperation.LoadLocal, new CilOperand.Index(1)),
            new(3, 4, CilOperation.LoadArrayLength, new CilOperand.None()),
        ];
        var condition = new StructuredBlockOccurrence(new(0), StructuredBlockRole.Owner);
        var exit = new StructuredSequence([new StructuredCode(new(new(1), StructuredBlockRole.Owner))]);
        StructuredRegion loop = postTest
            ? new StructuredPostTestLoop(StructuredSequence.Empty, condition, true, StructuredSequence.Empty, exit)
            : new StructuredLoop(condition, true, StructuredSequence.Empty, StructuredSequence.Empty, exit);
        return new(new(definition, null, 1, [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
                [array, array], instructions), new(0),
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
                .Add(new(0), new(new(0), 0, instructions[..2], [], new StructuredFallthroughExit(null)) { EndOffset = 2 })
                .Add(new(1), new(new(1), 2, instructions[2..], [], new StructuredFallthroughExit(null)) { EndOffset = 4 }),
            new([loop]), [], [],
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty
                .Add(0, []).Add(1, [CliValueKind.ManagedReference])
                .Add(2, []).Add(3, [CliValueKind.ManagedReference]));
    }

    private sealed class Arguments : IArgumentSignatureTypeResolver
    {
        public CliTypeIdentity Resolve(StructuredMethodHeader header, int index) =>
            header.Method.Signature.ParameterSignatureTypes[index];
    }
}
