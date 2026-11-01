using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ValueTypeFieldReadRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("ValueTypeFieldRead.Tests");
    private static readonly CliTypeIdentity Value =
        CliTypeIdentity.Named(Assembly, "Fixtures", "Value", isValueType: true);
    private static readonly CliTypeIdentity GenericValue =
        CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "Fixtures", "Value`1", isValueType: true),
            [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RewriteUsesAddressForAdjacentValueLocalFieldRead(bool generic)
    {
        var valueType = generic ? GenericValue : Value;
        var body = Body(
            Method(isStatic: true),
            [valueType],
            Load(0, CilOperation.LoadLocal, 0),
            FieldLoad(1, valueType));

        var rewritten = Rewrite(body);

        Assert.Equal(CilOperation.LoadLocalAddress, rewritten.Instructions[0].Operation);
        Assert.Equal(new CilOperand.Index(0), rewritten.Instructions[0].Operand);
        Assert.Equal(CilOperation.LoadField, rewritten.Instructions[1].Operation);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void RewriteUsesAddressForValueParameterWithCorrectInstanceIndex(
        bool isStatic,
        int argumentIndex)
    {
        var method = Method(isStatic, Value);
        var body = Body(
            method,
            [],
            Load(0, CilOperation.LoadArgument, argumentIndex),
            FieldLoad(1));

        var rewritten = Rewrite(body);

        Assert.Equal(CilOperation.LoadArgumentAddress, rewritten.Instructions[0].Operation);
    }

    [Fact]
    public void RewriteUsesConstructedMethodSignatureForGenericValueParameter()
    {
        var definition = Method(
            isStatic: true,
            CliTypeIdentity.GenericParameter(method: true, 0));
        var body = Body(
            definition,
            [],
            Load(0, CilOperation.LoadArgument, 0),
            FieldLoad(1)) with
        {
            MethodInstance = new MethodInstanceModel(
                definition,
                CliTypeIdentity.Named(Assembly, "Fixtures", "Host", isValueType: false),
                [Value],
                MethodSignatureModel.Create(
                    CliTypeIdentity.FromStackKind(CliValueKind.I4),
                    Value)),
        };

        var rewritten = Rewrite(body);

        Assert.Equal(CilOperation.LoadArgumentAddress, rewritten.Instructions[0].Operation);
    }

    [Fact]
    public void RewriteLeavesValueTypeThisReceiverAlone()
    {
        var method = Method(isStatic: false);
        var body = Body(
            method,
            [],
            Load(0, CilOperation.LoadArgument, 0),
            FieldLoad(1)) with
        {
            MethodInstance = new MethodInstanceModel(
                method,
                Value,
                [],
                method.Signature),
        };

        var rewritten = Rewrite(body);

        Assert.Equal(CilOperation.LoadArgument, rewritten.Instructions[0].Operation);
    }

    [Theory]
    [InlineData(CilOperation.LoadLocal, CilValueSource.ScalarLocal)]
    [InlineData(CilOperation.LoadArgument, CilValueSource.ScalarArgument)]
    [InlineData(CilOperation.LoadLocal, CilValueSource.NonFieldConsumer)]
    [InlineData(CilOperation.LoadLocal, CilValueSource.NonAdjacentField)]
    [InlineData(CilOperation.LoadLocal, CilValueSource.FieldAddressConsumer)]
    public void RewriteLeavesUnsupportedShapesAlone(
        CilOperation loadOperation,
        CilValueSource source)
    {
        var localTypes = source == CilValueSource.ScalarLocal
            ? ImmutableArray.Create(CliTypeIdentity.FromStackKind(CliValueKind.I4))
            : ImmutableArray.Create(Value);
        var parameterType = source == CilValueSource.ScalarArgument
            ? CliTypeIdentity.FromStackKind(CliValueKind.I4)
            : Value;
        var consumerOperation = source switch
        {
            CilValueSource.NonFieldConsumer => CilOperation.Pop,
            CilValueSource.FieldAddressConsumer => CilOperation.LoadFieldAddress,
            _ => CilOperation.LoadField,
        };
        var consumerOffset = source == CilValueSource.NonAdjacentField ? 2 : 1;
        var body = Body(
            Method(isStatic: true, parameterType),
            localTypes,
            Load(0, loadOperation, 0),
            new CilInstruction(
                consumerOffset,
                consumerOffset + 1,
                consumerOperation,
                consumerOperation == CilOperation.Pop
                    ? new CilOperand.None()
                    : FieldOperand()));

        var rewritten = Rewrite(body);

        Assert.Equal(loadOperation, rewritten.Instructions[0].Operation);
    }

    [Fact]
    public void RewriteLeavesFieldReadAtBranchTargetAlone()
    {
        var body = Body(
            Method(isStatic: true),
            [Value],
            Load(0, CilOperation.LoadLocal, 0),
            FieldLoad(1),
            new CilInstruction(
                2,
                3,
                CilOperation.Branch,
                new CilOperand.BranchTarget(1)));

        var rewritten = Rewrite(body);

        Assert.Equal(CilOperation.LoadLocal, rewritten.Instructions[0].Operation);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(40)]
    public void RewriteLeavesFieldReadAtExceptionBoundaryAlone(int fieldOffset)
    {
        var body = Body(
            Method(isStatic: true),
            [Value],
            Load(fieldOffset - 1, CilOperation.LoadLocal, 0),
            FieldLoad(fieldOffset)) with
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Filter,
                    10,
                    10,
                    30,
                    10,
                    null,
                    20),
            ],
        };

        var rewritten = Rewrite(body);

        Assert.Equal(CilOperation.LoadLocal, rewritten.Instructions[0].Operation);
    }

    [Fact]
    public void RewriteRejectsNullBodyThroughItsInterface()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((IMethodRewriteRule)new ValueTypeFieldReadRewriter()).Rewrite(null!));
    }

    [Fact]
    public void RewriteReturnsShortBodyUnchanged()
    {
        var body = Body(
            Method(isStatic: true),
            [Value],
            Load(0, CilOperation.LoadLocal, 0));

        Assert.Same(body, Rewrite(body));
    }

    [Theory]
    [InlineData(CilOperation.LoadLocal, 1)]
    [InlineData(CilOperation.LoadArgument, 1)]
    [InlineData(CilOperation.StoreLocal, 0)]
    [InlineData(CilOperation.Nop, 0)]
    public void RewriteLeavesInvalidOrUnrelatedValueLoadsAlone(
        CilOperation operation,
        int index)
    {
        var operand = operation == CilOperation.Nop
            ? (CilOperand)new CilOperand.None()
            : new CilOperand.Index(index);
        var body = Body(
            Method(isStatic: true, Value),
            [Value],
            new CilInstruction(0, 1, operation, operand),
            FieldLoad(1));

        var rewritten = Rewrite(body);

        Assert.Equal(operation, rewritten.Instructions[0].Operation);
    }

    [Theory]
    [InlineData(CilOperation.Nop)]
    [InlineData(CilOperation.Duplicate)]
    [InlineData(CilOperation.Call)]
    [InlineData(CilOperation.Volatile)]
    public void RewriteDoesNotCrossInterveningInstruction(CilOperation operation)
    {
        var body = Body(
            Method(isStatic: true),
            [Value],
            Load(0, CilOperation.LoadLocal, 0),
            new CilInstruction(1, 2, operation, new CilOperand.None()),
            FieldLoad(2));

        var rewritten = Rewrite(body);

        Assert.Equal(CilOperation.LoadLocal, rewritten.Instructions[0].Operation);
    }

    [Theory]
    [InlineData(CliValueKind.I4)]
    [InlineData(CliValueKind.ManagedReference)]
    [InlineData(CliValueKind.ValueType)]
    public void RewritePreservesFinalFieldLoadForEveryFieldStorage(
        CliValueKind fieldKind)
    {
        var fieldType = fieldKind == CliValueKind.ValueType
            ? GenericValue
            : CliTypeIdentity.FromStackKind(fieldKind);
        var body = Body(
            Method(isStatic: true),
            [Value],
            Load(0, CilOperation.LoadLocal, 0),
            FieldLoad(1, Value, fieldType));

        var rewritten = Rewrite(body);

        Assert.Equal(CilOperation.LoadLocalAddress, rewritten.Instructions[0].Operation);
        Assert.Equal(CilOperation.LoadField, rewritten.Instructions[1].Operation);
        var field = Assert.IsType<CilOperand.FieldInstance>(
            rewritten.Instructions[1].Operand);
        Assert.Equal(fieldType, field.Value.FieldType);
    }

    private static CilMethodBody Rewrite(CilMethodBody body) =>
        ((IMethodRewriteRule)new ValueTypeFieldReadRewriter()).Rewrite(body);

    private static CilMethodBody Body(
        MethodDefinitionModel method,
        ImmutableArray<CliTypeIdentity> locals,
        params CilInstruction[] instructions) => new(
        method,
        2,
        [.. locals.Select(type => type.StackKind)],
        [.. instructions])
        {
            LocalSignatureTypes = locals,
        };

    private static MethodDefinitionModel Method(
        bool isStatic,
        params CliTypeIdentity[] parameters) => new(
        new EntityKey(Assembly, 0x06000001),
        new EntityKey(Assembly, 0x02000001),
        "Run",
        isStatic,
        MethodSignatureModel.Create(
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            parameters),
        1);

    private static CilInstruction Load(
        int offset,
        CilOperation operation,
        int index) => new(
        offset,
        offset + 1,
        operation,
        new CilOperand.Index(index));

    private static CilInstruction FieldLoad(
        int offset,
        CliTypeIdentity? declaringType = null,
        CliTypeIdentity? fieldType = null) => new(
        offset,
        offset + 1,
        CilOperation.LoadField,
        FieldOperand(declaringType, fieldType));

    private static CilOperand.FieldInstance FieldOperand(
        CliTypeIdentity? declaringType = null,
        CliTypeIdentity? fieldType = null)
    {
        declaringType ??= Value;
        fieldType ??= CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var field = new FieldDefinitionModel(
            new EntityKey(Assembly, 0x04000001),
            new EntityKey(Assembly, 0x02000003),
            "Field",
            fieldType,
            false);
        return new CilOperand.FieldInstance(new FieldInstanceModel(field, declaringType,
            field.SignatureType));
    }

    public enum CilValueSource
    {
        ScalarLocal,
        ScalarArgument,
        NonFieldConsumer,
        NonAdjacentField,
        FieldAddressConsumer,
    }
}
