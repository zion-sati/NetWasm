namespace NetWasm.Compiler.Core.Tests;

public sealed class CilSafepointClassifierTests
{
    private static readonly AssemblyIdentity Assembly = new("Safepoints");

    [Theory]
    [InlineData(CilOperation.NewObject)]
    [InlineData(CilOperation.NewArray)]
    [InlineData(CilOperation.NewRectangularArray)]
    [InlineData(CilOperation.NewBoundedRectangularArray)]
    [InlineData(CilOperation.Box)]
    [InlineData(CilOperation.DelegateCombine)]
    [InlineData(CilOperation.DelegateRemove)]
    [InlineData(CilOperation.MaterializeType)]
    [InlineData(CilOperation.GetObjectType)]
    [InlineData(CilOperation.CallIndirect)]
    public void AllocatingOperationsAlwaysRequireARootDecision(CilOperation operation)
    {
        Assert.True(CilSafepointClassifier.RequiresUnconditionalRootDecision(
            Instruction(operation)));
        Assert.True(CilSafepointClassifier.MayTransferControlExceptionally(
            Instruction(operation)));
    }

    [Theory]
    [InlineData(CilOperation.LoadRectangularArrayElement)]
    [InlineData(CilOperation.LoadRectangularArrayElementAddress)]
    [InlineData(CilOperation.StoreRectangularArrayElement)]
    public void RectangularAccessRetainsExceptionalLiveness(CilOperation operation)
    {
        Assert.True(CilSafepointClassifier.MayTransferControlExceptionally(Instruction(operation)));
        Assert.False(CilSafepointClassifier.RequiresUnconditionalRootDecision(Instruction(operation)));
    }

    [Fact]
    public void NonAllocatingCallsOnlyNeedAFrameWhenTheyCanResumeLocally()
    {
        var instruction = Instruction(CilOperation.Call, offset: 2);
        var unprotected = Body(instruction);
        var protectedBody = Body(instruction) with
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Finally,
                    10,
                    1,
                    11,
                    1,
                    null,
                    null),
                new CilExceptionRegion(
                    CilExceptionRegionKind.Catch,
                    2,
                    1,
                    3,
                    1,
                    new EntityKey(Assembly, 0x02000002),
                    null),
            ],
        };

    }

    [Fact]
    public void OnlyCheckedNumericConversionsCanTransferExceptionally()
    {
        var checkedConversion = new CilInstruction(
            0,
            1,
            CilOperation.ConvertNumeric,
            new CilOperand.NumericConversion(32, false, true, false, false));
        var uncheckedConversion = checkedConversion with
        {
            Operand = new CilOperand.NumericConversion(
                32,
                false,
                false,
                false,
                false),
        };

        Assert.True(CilSafepointClassifier.MayTransferControlExceptionally(
            checkedConversion));
        Assert.False(CilSafepointClassifier.MayTransferControlExceptionally(
            uncheckedConversion));
        Assert.False(CilSafepointClassifier.MayTransferControlExceptionally(
            Instruction(CilOperation.ConvertNumeric)));
        Assert.False(CilSafepointClassifier.MayTransferControlExceptionally(
            Instruction(CilOperation.Add)));
    }




    private static MethodDefinitionModel MethodDefinition(
        EntityKey key,
        string name,
        bool hasBody,
        EntityKey? declaringType = null) => new(
            key,
            declaringType ?? new EntityKey(Assembly, 0x02000001),
            name,
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            hasBody ? 1 : 0);

    private static MethodInstanceModel MethodInstance(
        int token,
        string name,
        CliTypeIdentity declaringType,
        bool hasBody = true)
    {
        var definition = MethodDefinition(
            new EntityKey(Assembly, token),
            name,
            hasBody,
            new EntityKey(Assembly, 0x02000002));
        return new MethodInstanceModel(
            definition,
            declaringType,
            [],
            definition.Signature);
    }

    private static CilInstruction Instruction(
        CilOperation operation,
        int offset = 0) => new(
            offset,
            offset + 1,
            operation,
            new CilOperand.None());

    private static CilMethodBody Body(CilInstruction instruction) => new(
        new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000001),
            new EntityKey(Assembly, 0x02000001),
            "Run",
            true,
            MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.Void)),
            1),
        1,
        [],
        [instruction]);

    private sealed class UnusedRepository :
        ITypeRepository,
        IFieldRepository,
        IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) =>
            throw new InvalidOperationException("Unexpected type lookup");

        public FieldDefinitionModel GetField(EntityKey key) =>
            throw new InvalidOperationException("Unexpected field lookup");

        public MethodDefinitionModel GetMethod(EntityKey key) =>
            throw new InvalidOperationException("Unexpected method lookup");
    }

    private sealed class Repository :
        ITypeRepository,
        IFieldRepository,
        IMethodRepository
    {
        public Dictionary<EntityKey, TypeDefinitionModel> Types { get; } = [];
        public Dictionary<EntityKey, FieldDefinitionModel> Fields { get; } = [];
        public Dictionary<EntityKey, MethodDefinitionModel> Methods { get; } = [];

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => Types[key];

        public FieldDefinitionModel GetField(EntityKey key) => Fields[key];

        public MethodDefinitionModel GetMethod(EntityKey key) => Methods[key];
    }
}
