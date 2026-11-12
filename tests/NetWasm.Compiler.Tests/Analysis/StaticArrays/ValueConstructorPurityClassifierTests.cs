using NetWasm.Compiler.Core;
using NetWasm.Compiler.Analysis.StaticArrays;

namespace NetWasm.Compiler.Tests.Analysis.StaticArrays;

using static StaticArrayTestSupport;

public sealed class ValueConstructorPurityClassifierTests
{
    [Fact]
    public void RejectsMissingDependenciesAndMethod()
    {
        var facts = new Facts();
        Assert.Throws<ArgumentNullException>(() => new ValueConstructorPurityClassifier(null!, facts, facts, facts, facts));
        Assert.Throws<ArgumentNullException>(() => new ValueConstructorPurityClassifier(facts, null!, facts, facts, facts));
        Assert.Throws<ArgumentNullException>(() => new ValueConstructorPurityClassifier(facts, facts, null!, facts, facts));
        Assert.Throws<ArgumentNullException>(() => new ValueConstructorPurityClassifier(facts, facts, facts, null!, facts));
        Assert.Throws<ArgumentNullException>(() => new ValueConstructorPurityClassifier(facts, facts, facts, facts, null!));
        Assert.Throws<ArgumentNullException>(() => facts.Purity().IsPure(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void RejectsNonConstructorAndImportedContracts(int kind)
    {
        var method = Constructor();
        method = method with
        {
            Definition = method.Definition with
            {
                IsStatic = kind == 0,
                Name = kind == 1 ? "Factory" : ".ctor",
                RelativeVirtualAddress = kind == 2 ? 0 : 1,
                JSImport = kind == 4 ? new("make", "fixture") : null,
                WitImport = kind == 5 ? new("fixture", "make") : null,
                NativeImport = kind == 6 ? new("fixture", "make", 0, false, false, null) : null,
            },
            Signature = kind == 3 ? MethodSignatureModel.Create(CliValueKind.I8, CliValueKind.I8) : method.Signature,
        };

        Assert.False(new Facts().Purity().IsPure(method));
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
    public void RejectsBodiesWithoutAnOwnFieldAssignmentProof(int kind)
    {
        var facts = new Facts();
        var body = facts.ConstructorBody;
        facts.ConstructorBody = kind switch
        {
            0 => body with { ExceptionRegions = [new(CilExceptionRegionKind.Finally, 0, 2, 2, 2, null, null)] },
            1 => body with { Instructions = [] },
            2 => body with { Instructions = body.Instructions.SetItem(3, I(3, CilOperation.Throw)) },
            3 => body with { Instructions = [I(0, CilOperation.LoadNull), I(1, CilOperation.Pop), I(2, CilOperation.Return)] },
            4 => body with { Instructions = body.Instructions.SetItem(0, I(0, CilOperation.LoadArgument, new CilOperand.Index(1))) },
            5 => body with { Instructions = body.Instructions.SetItem(1, I(1, CilOperation.LoadInt64, new CilOperand.ConstantI8(0))) },
            6 => body with { Instructions = body.Instructions.SetItem(1, I(1, CilOperation.LoadArgument, new CilOperand.Index(0))) },
            _ => body with { Instructions = body.Instructions.SetItem(1, I(1, CilOperation.LoadArgument, new CilOperand.Index(2))) },
        };

        Assert.False(facts.Purity().IsPure(Constructor()));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    public void ValidatesResolvedFieldOwnerStorageAndParameterKind(int kind, bool expected)
    {
        var method = Constructor();
        var facts = new Facts();
        var field = Value with
        {
            IsStatic = kind == 1,
            DeclaringType = kind == 2 ? HolderKey : ElementKey,
            FieldType = kind == 3 ? CliValueKind.ManagedReference : kind == 4 ? CliValueKind.I4 : CliValueKind.I8,
        };
        var instance = new FieldInstanceModel(field, kind == 5 ? Holder : Element, field.SignatureType);
        facts.ConstructorBody = facts.ConstructorBody with
        {
            Instructions = facts.ConstructorBody.Instructions.SetItem(2,
                I(2, CilOperation.StoreField, new CilOperand.FieldInstance(instance))),
        };

        Assert.Equal(expected, facts.Purity().IsPure(method));
    }

    [Fact]
    public void RecognizesOnlyAssignmentsToTheConstructedValue()
    {
        var facts = new Facts();
        Assert.True(facts.Purity().IsPure(Constructor()));
        facts.ConstructorBody = facts.ConstructorBody with
        {
            Instructions = facts.ConstructorBody.Instructions.SetItem(2, I(2, CilOperation.StoreStaticField, new CilOperand.Entity(Value.Key))),
        };
        Assert.False(facts.Purity().IsPure(Constructor()));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void PreservesExplicitInitialization(bool beforeFieldInit, bool expected)
    {
        var facts = new Facts { HasInitializer = true, BeforeFieldInit = beforeFieldInit };
        Assert.Equal(expected, facts.Purity().IsPure(Constructor()));
    }

    [Fact]
    public void PreservesNoInliningAndReferenceConstruction()
    {
        var method = Constructor();
        var facts = new Facts();
        Assert.False(facts.Purity().IsPure(method with { Definition = method.Definition with { IsNoInlining = true } }));
        Assert.False(facts.Purity().IsPure(method with { DeclaringType = Holder }));
        facts.IsIntrinsic = true;
        Assert.False(facts.Purity().IsPure(method));
    }
}
