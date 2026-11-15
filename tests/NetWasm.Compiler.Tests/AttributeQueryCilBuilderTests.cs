// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeQueryCilBuilderTests
{
    private static readonly AssemblyIdentity Assembly = new("AttributeQueries");
    private static readonly CliTypeIdentity AttributeType = CliTypeIdentity.Named(
        Assembly, "Example", "MarkerAttribute", false);
    private static readonly CliTypeIdentity ResultArray = CliTypeIdentity.SzArray(AttributeType);
    private static readonly CliTypeIdentity TargetType = CliTypeIdentity.Named(
        Assembly, "Example", "Decorated", false);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void IsDefinedConsumesArgumentsWithoutMaterializingAttributes(bool present, bool inherit)
    {
        var constructions = new RecordingConstructions();
        var request = Request(AttributeQueryOperation.IsDefined, present ? [Attribute(1)] : []) with
        {
            InheritConstant = inherit,
        };

        var fragment = Build(new AttributeQueryCilBuilder(constructions), request);

        Assert.Empty(constructions.Requests);
        Assert.Empty(fragment.LocalTypes);
        Assert.Equal([CilOperation.Pop, CilOperation.Pop, CilOperation.Pop, CilOperation.LoadInt32],
            fragment.Instructions.Select(instruction => instruction.Operation));
        Assert.Equal(new CilOperand.ConstantI4(present ? 1 : 0), fragment.Instructions[^1].Operand);
        Assert.Equal(3, fragment.MaxStack);
    }

    [Fact]
    public void SingularAbsenceReturnsNullAfterConsumingArguments()
    {
        var constructions = new RecordingConstructions();
        var fragment = Build(new AttributeQueryCilBuilder(constructions), Request(AttributeQueryOperation.GetOne, []));

        Assert.Empty(constructions.Requests);
        Assert.Equal(CilOperation.LoadNull, fragment.Instructions[^1].Operation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstantInheritanceBuildsOnlyItsSelectedResults(bool inherit)
    {
        var constructions = new RecordingConstructions();
        var direct = Attribute(1);
        var inherited = Attribute(2);
        var request = Request(AttributeQueryOperation.GetOne, [direct]) with
        {
            Selection = new(TargetType, AttributeType, new AttributeRetrievalResult([direct]), new AttributeRetrievalResult([inherited])),
            InheritConstant = inherit,
            FirstLocal = 7,
        };

        var fragment = Build(new AttributeQueryCilBuilder(constructions), request);

        Assert.Equal((inherit ? inherited : direct, 7), Assert.Single(constructions.Requests));
        Assert.Equal(AttributeType, Assert.Single(fragment.LocalTypes));
        Assert.DoesNotContain(fragment.Instructions, instruction => instruction.Operation == CilOperation.Branch);
        Assert.Equal(new CilOperand.Index(7), fragment.Instructions[^1].Operand);
    }

    [Fact]
    public void RuntimeInheritanceSavesTheBooleanAndBranchesToSeparateBoundedResults()
    {
        var constructions = new RecordingConstructions();
        var direct = Attribute(1);
        var inherited = Attribute(2);
        var request = Request(AttributeQueryOperation.GetOne, [direct]) with
        {
            Selection = new(TargetType, AttributeType, new AttributeRetrievalResult([direct]), new AttributeRetrievalResult([inherited])),
            InheritConstant = null,
            InheritArgument = 1,
            FirstLocal = 4,
        };

        var fragment = Build(new AttributeQueryCilBuilder(constructions), request);

        Assert.Equal(CilOperation.Pop, fragment.Instructions[0].Operation);
        Assert.Equal(CilOperation.StoreLocal, fragment.Instructions[1].Operation);
        Assert.Equal(new CilOperand.Index(4), fragment.Instructions[1].Operand);
        Assert.Equal(CilOperation.Pop, fragment.Instructions[2].Operation);
        Assert.Equal([(direct, 5), (inherited, 6)], constructions.Requests);
        var conditional = Assert.Single(fragment.Instructions,
            instruction => instruction.Operation == CilOperation.BranchIfTrue);
        var inheritedStart = ((CilOperand.BranchTarget)conditional.Operand).Offset;
        Assert.Equal(new CilOperand.MethodInstance(inherited.Constructor),
            fragment.Instructions[inheritedStart].Operand);
        var branch = Assert.Single(fragment.Instructions,
            instruction => instruction.Operation == CilOperation.Branch);
        Assert.Equal(new CilOperand.BranchTarget(fragment.Instructions.Length - 1), branch.Operand);
        Assert.Equal(CilOperation.Nop, fragment.Instructions[^1].Operation);
        Assert.Equal(Enumerable.Range(0, fragment.Instructions.Length),
            fragment.Instructions.Select(instruction => instruction.Offset));
        Assert.Equal(Enumerable.Range(1, fragment.Instructions.Length),
            fragment.Instructions.Select(instruction => instruction.NextOffset));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PluralQueriesAllocateAndPopulateAnOrdinaryTypedArray(int count)
    {
        var constructions = new RecordingConstructions();
        var attributes = Enumerable.Range(1, count).Select(Attribute).ToImmutableArray();
        var fragment = Build(new AttributeQueryCilBuilder(constructions),
            Request(AttributeQueryOperation.GetMany, attributes));

        Assert.Equal(new CilOperand.TypeIdentity(AttributeType),
            Assert.Single(fragment.Instructions, instruction => instruction.Operation == CilOperation.NewArray).Operand);
        Assert.Equal(new CilOperand.ConstantI4(count), fragment.Instructions[3].Operand);
        Assert.Equal(count, fragment.Instructions.Count(instruction => instruction.Operation == CilOperation.StoreArrayElementReference));
        Assert.Equal(attributes, constructions.Requests.Select(request => request.Attribute));
        Assert.Equal(ResultArray, fragment.LocalTypes[0]);
        Assert.Equal(new CilOperand.Index(0), fragment.Instructions[^1].Operand);
    }

    [Fact]
    public void SingularAmbiguityMaterializesAndRetainsAllResultsBeforeThrowing()
    {
        var constructions = new RecordingConstructions();
        var attributes = ImmutableArray.Create(Attribute(1), Attribute(2));
        var exceptionConstructor = Attribute(3).Constructor;
        var request = Request(AttributeQueryOperation.GetOne, attributes) with
        {
            AmbiguityConstructor = exceptionConstructor,
        };

        var fragment = Build(new AttributeQueryCilBuilder(constructions), request);

        Assert.Equal(attributes, constructions.Requests.Select(item => item.Attribute));
        Assert.Equal(2, fragment.Instructions.Count(item => item.Operation == CilOperation.StoreArrayElementReference));
        Assert.Equal(ResultArray, fragment.LocalTypes[0]);
        Assert.Equal(CilOperation.NewObject, fragment.Instructions[^2].Operation);
        Assert.Equal(new CilOperand.MethodInstance(exceptionConstructor), fragment.Instructions[^2].Operand);
        Assert.Equal(CilOperation.Throw, fragment.Instructions[^1].Operation);
    }

    [Fact]
    public void InvalidRequestsFailBeforeMaterialization()
    {
        var constructions = new RecordingConstructions();
        var builder = new AttributeQueryCilBuilder(constructions);
        var valid = Request(AttributeQueryOperation.GetOne, [Attribute(1)]);

        Assert.Throws<ArgumentNullException>(() => new AttributeQueryCilBuilder(null!));
        Assert.Throws<ArgumentNullException>(() => Build(builder, null!));
        Assert.Throws<ArgumentNullException>(() => Build(builder, valid with { Selection = null! }));
        Assert.Throws<ArgumentNullException>(() => Build(builder, valid with { ResultArrayType = null! }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(builder, valid with { FirstLocal = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(builder, valid with { ArgumentCount = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(builder, valid with { Operation = (AttributeQueryOperation)999 }));
        Assert.Throws<ArgumentException>(() => Build(builder, valid with { ResultArrayType = AttributeType }));
        Assert.Throws<ArgumentException>(() => Build(builder, valid with { InheritArgument = -1 }));
        Assert.Throws<ArgumentException>(() => Build(builder, valid with { InheritArgument = 3 }));
        Assert.Throws<ArgumentException>(() => Build(builder, valid with { InheritArgument = null, InheritConstant = null }));
        Assert.Throws<ArgumentException>(() => Build(builder, valid with
        {
            Selection = valid.Selection with { Direct = new AttributeExistenceResult(true) },
        }));
        Assert.Throws<ArgumentException>(() => Build(builder, valid with
        {
            Selection = valid.Selection with { Inherited = new AttributeExistenceResult(true) },
        }));
        Assert.Throws<ArgumentException>(() => Build(builder,
            Request(AttributeQueryOperation.GetOne, [Attribute(1), Attribute(2)])));
        Assert.Empty(constructions.Requests);
    }

    private static AttributeQueryCilRequest Request(AttributeQueryOperation operation,
        ImmutableArray<AttributeConstructionPlan> attributes)
    {
        AttributeQueryResult result = operation == AttributeQueryOperation.IsDefined
            ? new AttributeExistenceResult(!attributes.IsEmpty) : new AttributeRetrievalResult(attributes);
        return new(operation, new(TargetType, AttributeType, result, result), 3, 2, false, ResultArray, null, 0);
    }

    private static AttributeConstructionPlan Attribute(int token)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000000 + token),
            new(Assembly, 0x02000001), ".ctor", false, signature, 1);
        return new(AttributeType, new(definition, AttributeType, [], signature), [], []);
    }

    private static AttributeCilFragment Build<TBuilder>(TBuilder builder, AttributeQueryCilRequest request)
        where TBuilder : IAttributeQueryCilBuilder => builder.Build(request);

    private sealed class RecordingConstructions : IAttributeConstructionCilBuilder
    {
        public List<(AttributeConstructionPlan Attribute, int FirstLocal)> Requests { get; } = [];

        public AttributeCilFragment Build(AttributeConstructionPlan attribute, int firstLocal)
        {
            Requests.Add((attribute, firstLocal));
            return new([
                new(0, 1, CilOperation.NewObject, new CilOperand.MethodInstance(attribute.Constructor)),
                new(1, 2, CilOperation.StoreLocal, new CilOperand.Index(firstLocal)),
                new(2, 3, CilOperation.LoadLocal, new CilOperand.Index(firstLocal)),
            ], [attribute.AttributeType], 1);
        }
    }
}
