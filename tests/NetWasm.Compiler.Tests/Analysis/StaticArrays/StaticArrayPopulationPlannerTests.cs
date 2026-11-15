// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Analysis.StaticArrays;

namespace NetWasm.Compiler.Tests.Analysis.StaticArrays;

using static StaticArrayTestSupport;

public sealed class StaticArrayPopulationPlannerTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void LoadedFieldsMustBeReadonlyOwnAndReferenceFree(int kind, bool expected)
    {
        var facts = new Facts();
        var type = kind == 2 ? Element : CliTypeIdentity.FromStackKind(kind == 1 ? CliValueKind.ManagedReference : CliValueKind.I8);
        var shared = new FieldDefinitionModel(new(Assembly, 0x04000003), HolderKey, "Shared", type, true) { IsInitOnly = kind != 3 };
        facts.Fields.Add(shared.Key, shared);
        if (kind == 2)
            facts.Fields[Value.Key] = Value with { IsStatic = true };
        var method = ScalarInitializer(facts, type, I(4, CilOperation.LoadStaticField,
            new CilOperand.FieldInstance(new(shared, Holder, type))));

        var removed = facts.Planner().Plan(method, []);

        if (expected)
            Assert.Equal([2, 3, 4, 7], removed.Order());
        else
            Assert.Empty(removed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoesNotRewriteOrdinaryMethodsOrExceptionRegions(bool exceptionRegion)
    {
        var initial = Initializer();
        var method = initial.Method with { Definition = initial.Method.Definition with { Name = exceptionRegion ? ".cctor" : "Build" } };
        var body = initial.Body with
        {
            Method = method.Definition,
            MethodInstance = method,
            Instructions = exceptionRegion
                ? [.. initial.Body.Instructions[..9], I(9, CilOperation.Leave, new CilOperand.BranchTarget(12)),
                    I(10, CilOperation.Nop), I(11, CilOperation.EndFinally), I(12, CilOperation.Return)]
                : initial.Body.Instructions,
            ExceptionRegions = exceptionRegion ? [new(CilExceptionRegionKind.Finally, 0, 10, 10, 2, null, null)] : [],
        };

        Assert.Empty(new Facts().Planner().Plan(Managed(body), []));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void DeclinesIncompleteOrEffectfulPopulationProofs(int kind)
    {
        var facts = new Facts();
        var initial = Initializer();
        var instructions = initial.Body.Instructions;
        instructions = kind switch
        {
            0 => instructions.SetItem(4, I(4, CilOperation.Call, new CilOperand.MethodInstance(Constructor()))),
            1 => instructions.SetItem(4, I(4, CilOperation.Nop)).SetItem(5, I(5, CilOperation.ConvertInt64)),
            2 => instructions.SetItem(4, I(4, CilOperation.LoadNull)),
            3 => instructions.SetItem(6, I(6, CilOperation.NewObject, new CilOperand.None())),
            4 => instructions.SetItem(4, I(4, CilOperation.Nop)).SetItem(5, I(5, CilOperation.Nop)),
            5 => instructions.SetItem(5, I(5, CilOperation.Nop)),
            6 => instructions.Add(I(10, CilOperation.Branch, new CilOperand.BranchTarget(7))),
            7 => instructions.SetItem(6, I(6, CilOperation.Nop)).SetItem(5, I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(7))),
            _ => instructions.SetItem(4, I(4, CilOperation.Nop)).SetItem(5, I(5, CilOperation.Nop)).SetItem(6, I(6, CilOperation.Nop)),
        };

        Assert.Empty(facts.Planner().Plan(Managed(initial.Body with { Instructions = instructions }), []));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RequiresMatchingValueAndStoreElementTypes(int kind)
    {
        var facts = new Facts();
        var initial = Initializer();
        var instructions = initial.Body.Instructions;
        if (kind == 0)
            instructions = instructions.SetItem(7, I(7, CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Holder)));
        else if (kind == 1)
            instructions = instructions.SetItem(6, I(6, CilOperation.Nop));
        else
        {
            var other = CliTypeIdentity.Named(Assembly, "Fixture", "OtherValue", true);
            facts.Fields[Table.Key] = new(Table.Key, HolderKey, "Table", CliTypeIdentity.SzArray(other), true) { IsInitOnly = true };
            instructions = instructions.SetItem(1, I(1, CilOperation.NewArray, new CilOperand.TypeIdentity(other)))
                .SetItem(7, I(7, CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(other)));
        }

        Assert.Empty(facts.Planner().Plan(Managed(initial.Body with { Instructions = instructions }), []));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ReferenceStoresRequireNullOrAProvenCompatibleString(int kind)
    {
        var facts = new Facts();
        var element = kind == 0 ? CliTypeIdentity.FromStackKind(CliValueKind.I4) : Holder;
        var method = ScalarInitializer(facts, element, kind == 1
            ? I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(42))
            : I(4, CilOperation.LoadString, new CilOperand.UserString("value")));
        method = Managed(method.Body with
        {
            Instructions = method.Body.Instructions.SetItem(7,
            I(7, CilOperation.StoreArrayElementReference))
        });

        Assert.Empty(facts.Planner().Plan(method, []));
    }

    [Fact]
    public void ResolvesClosedFieldInstancesAndDeclinesUnknownFieldOperands()
    {
        var facts = new Facts();
        var initial = Initializer();
        var field = new FieldInstanceModel(Table, Holder, Table.SignatureType);
        var body = initial.Body with
        {
            Instructions = initial.Body.Instructions.SetItem(8,
            I(8, CilOperation.StoreStaticField, new CilOperand.FieldInstance(field)))
        };
        Assert.NotEmpty(facts.Planner().Plan(Managed(body), []));
        body = body with { Instructions = body.Instructions.SetItem(8, I(8, CilOperation.StoreStaticField)) };
        Assert.Empty(facts.Planner().Plan(Managed(body), []));
    }

    [Fact]
    public void RejectsMissingProofCapabilitiesAndInputs()
    {
        var facts = new Facts();
        Assert.Throws<ArgumentNullException>(() => new StaticArrayPopulationPlanner(null!, facts, facts, facts.Purity(), facts, facts));
        Assert.Throws<ArgumentNullException>(() => new StaticArrayPopulationPlanner(facts, null!, facts, facts.Purity(), facts, facts));
        Assert.Throws<ArgumentNullException>(() => new StaticArrayPopulationPlanner(facts, facts, null!, facts.Purity(), facts, facts));
        Assert.Throws<ArgumentNullException>(() => new StaticArrayPopulationPlanner(facts, facts, facts, null!, facts, facts));
        Assert.Throws<ArgumentNullException>(() => new StaticArrayPopulationPlanner(facts, facts, facts, facts.Purity(), null!, facts));
        Assert.Throws<ArgumentNullException>(() => new StaticArrayPopulationPlanner(facts, facts, facts, facts.Purity(), facts, null!));
        Assert.Throws<ArgumentNullException>(() => facts.Planner().Plan(null!, []));
        Assert.Throws<ArgumentNullException>(() => facts.Planner().Plan(Initializer(), null!));
    }

    [Theory]
    [InlineData(CilOperation.LoadInt32, CliValueKind.I4)]
    [InlineData(CilOperation.LoadInt64, CliValueKind.I8)]
    [InlineData(CilOperation.LoadFloat32, CliValueKind.F4)]
    [InlineData(CilOperation.LoadFloat64, CliValueKind.F8)]
    [InlineData(CilOperation.LoadNull, CliValueKind.ManagedReference)]
    [InlineData(CilOperation.LoadString, CliValueKind.ManagedReference)]
    public void ProvesLiteralScalarAndReferencePopulations(CilOperation operation, CliValueKind kind)
    {
        var facts = new Facts();
        var element = operation == CilOperation.LoadString
            ? CliTypeIdentity.Primitive("string", kind, false) : CliTypeIdentity.FromStackKind(kind);
        var method = ScalarInitializer(facts, element, I(4, operation, operation switch
        {
            CilOperation.LoadInt32 => new CilOperand.ConstantI4(42),
            CilOperation.LoadInt64 => new CilOperand.ConstantI8(42),
            CilOperation.LoadFloat32 => new CilOperand.ConstantF4(42),
            CilOperation.LoadFloat64 => new CilOperand.ConstantF8(42),
            CilOperation.LoadString => new CilOperand.UserString("value"),
            _ => new CilOperand.None(),
        }));

        Assert.Equal([2, 3, 4, 7], facts.Planner().Plan(method, []).Order());
    }

    [Theory]
    [InlineData(CilOperation.ConvertInt32, CliValueKind.I4)]
    [InlineData(CilOperation.ConvertInt32Unsigned, CliValueKind.I4)]
    [InlineData(CilOperation.ConvertInt64, CliValueKind.I8)]
    [InlineData(CilOperation.ConvertInt64Unsigned, CliValueKind.I8)]
    [InlineData(CilOperation.ConvertFloat32, CliValueKind.F4)]
    [InlineData(CilOperation.ConvertFloat64, CliValueKind.F8)]
    public void ProvesOnlyTheResultKindOfNonThrowingScalarConversions(CilOperation operation, CliValueKind kind)
    {
        var facts = new Facts();
        var method = ScalarInitializer(facts, CliTypeIdentity.FromStackKind(kind),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(42)));
        method = Managed(method.Body with { Instructions = method.Body.Instructions.SetItem(5, I(5, operation)) });

        Assert.Equal([2, 3, 4, 5, 7], facts.Planner().Plan(method, []).Order());
    }

    private static ManagedMethodBody ScalarInitializer(Facts facts,
        CliTypeIdentity element, CilInstruction producer)
    {
        var initial = Initializer();
        facts.Fields[Table.Key] = new(Table.Key, HolderKey, "Table", CliTypeIdentity.SzArray(element), true) { IsInitOnly = true };
        return Managed(initial.Body with
        {
            Instructions = initial.Body.Instructions
                .SetItem(1, I(1, CilOperation.NewArray, new CilOperand.TypeIdentity(element)))
                .SetItem(4, producer).SetItem(5, I(5, CilOperation.Nop)).SetItem(6, I(6, CilOperation.Nop))
                .SetItem(7, I(7, element.IsValueType ? CilOperation.StoreArrayElement : CilOperation.StoreArrayElementReference,
                    new CilOperand.TypeIdentity(element))),
        });
    }

    [Fact]
    public void RemovesOnlyPopulationAndPreservesAllocationLengthAndStaticStore()
    {
        var removed = new Facts().Planner().Plan(Initializer(), []);
        Assert.Equal(Enumerable.Range(2, 6), removed.Order());
        Assert.DoesNotContain(0, removed);
        Assert.DoesNotContain(1, removed);
        Assert.DoesNotContain(8, removed);
        Assert.DoesNotContain(9, removed);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 1)]
    public void KeepsPotentiallyThrowingLengthAndIndexCases(int length, int index)
    {
        Assert.Empty(new Facts().Planner().Plan(Initializer(length, index), []));
    }

    [Fact]
    public void KeepsObservedFieldsAndEffectfulConstructors()
    {
        var facts = new Facts();
        var initializer = Initializer();
        Assert.Empty(facts.Planner().Plan(initializer, [Table.Key]));
        facts.HasInitializer = true;
        Assert.Empty(facts.Planner().Plan(initializer, []));
    }

    [Fact]
    public void KeepsEscapingArrayReferencesAndCrossBlockPopulations()
    {
        var initializer = Initializer();
        var escaped = initializer.Body with
        {
            Instructions = initializer.Body.Instructions.SetItem(2, I(2, CilOperation.StoreLocal, new CilOperand.Index(0))),
        };
        Assert.Empty(new Facts().Planner().Plan(Managed(escaped), []));
        var split = initializer.Body with
        {
            Instructions = initializer.Body.Instructions.Insert(2, new CilInstruction(10, 2, CilOperation.Branch, new CilOperand.BranchTarget(2))),
        };
        Assert.Empty(new Facts().Planner().Plan(Managed(split), []));
    }

    [Fact]
    public void DoesNotRemoveManagedReferencesOrOtherOwnersInitializationFromLoadedValues()
    {
        var facts = new Facts();
        var shared = new FieldDefinitionModel(new(Assembly, 0x04000003), HolderKey, "Shared", Element, true) { IsInitOnly = true };
        facts.Fields.Add(shared.Key, shared);
        var initial = Initializer();
        var body = initial.Body with
        {
            Instructions = initial.Body.Instructions
                .SetItem(4, I(4, CilOperation.LoadStaticField, new CilOperand.Entity(shared.Key)))
                .SetItem(5, I(5, CilOperation.Nop)).SetItem(6, I(6, CilOperation.Nop)),
        };
        Assert.NotEmpty(facts.Planner().Plan(Managed(body), []));
        facts.Fields[Value.Key] = Value with { FieldType = CliValueKind.ManagedReference };
        Assert.Empty(facts.Planner().Plan(Managed(body), []));
        facts.Fields[Value.Key] = Value;
        facts.Fields[shared.Key] = shared with { DeclaringType = ElementKey };
        Assert.Empty(facts.Planner().Plan(Managed(body), []));
    }
}
