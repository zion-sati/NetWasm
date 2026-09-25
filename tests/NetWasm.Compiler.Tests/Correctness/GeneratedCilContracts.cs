using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal abstract record GeneratedCilOperand
{
    private GeneratedCilOperand()
    {
    }

    public sealed record None : GeneratedCilOperand;

    public sealed record Int32(int Value) : GeneratedCilOperand;

    public sealed record Int64(long Value) : GeneratedCilOperand;

    public sealed record Float32(float Value) : GeneratedCilOperand;

    public sealed record Float64(double Value) : GeneratedCilOperand;

    public sealed record Index(int Value) : GeneratedCilOperand;

    public sealed record BlockTarget(int BlockId) : GeneratedCilOperand;

    public sealed record BlockTargets(ImmutableArray<int> BlockIds) :
        GeneratedCilOperand;

    public sealed record NumericConversion(
        int BitWidth,
        bool DestinationUnsigned,
        bool Checked,
        bool SourceUnsigned,
        bool Native) : GeneratedCilOperand;

    public sealed record MetadataToken(
        int Value,
        CliValueKind StackKind,
        ImmutableArray<CliValueKind> ParameterTypes,
        CliValueKind ReturnType,
        bool IsStatic,
        bool IsConstructor,
        bool IsVirtual = false) : GeneratedCilOperand;
}

internal sealed record GeneratedCilInstruction(
    CilOperation Operation,
    GeneratedCilOperand Operand);

internal sealed record GeneratedCilBlock(
    int Id,
    ImmutableArray<CliValueKind> EntryStack,
    ImmutableArray<GeneratedExceptionRegionMembership> ExceptionRegionPath,
    ImmutableArray<GeneratedCilInstruction> Instructions);

internal enum GeneratedExceptionRegionPart
{
    Try,
    Filter,
    Handler,
}

internal sealed record GeneratedExceptionRegionMembership(
    int RegionId,
    GeneratedExceptionRegionPart Part);

internal sealed record GeneratedCilExceptionRegion(
    int Id,
    CilExceptionRegionKind Kind,
    int TryStartBlock,
    int TryEndBlock,
    int HandlerStartBlock,
    int HandlerEndBlock,
    int? FilterStartBlock = null,
    int? CatchTypeToken = null);

internal sealed record GeneratedCilProgram(
    int Seed,
    CliValueKind ReturnType,
    ImmutableArray<CliValueKind> Arguments,
    ImmutableArray<CliValueKind> Locals,
    ImmutableArray<GeneratedCilBlock> Blocks)
{
    public ImmutableArray<GeneratedCilExceptionRegion> ExceptionRegions { get; init; } =
        [];

    public ImmutableArray<CilOperation> Operations =>
        [.. Blocks.SelectMany(block => block.Instructions)
            .Select(instruction => instruction.Operation)
            .Distinct()
            .Order()];
}

internal interface IRandomCilGenerator
{
    GeneratedCilProgram Generate(int seed);

    GeneratedCilProgram GenerateMetadata(
        int seed,
        RandomCilMetadataTokens tokens);

    GeneratedCilProgram GenerateExceptionHandling(int seed);

    GeneratedCilProgram GenerateControlFlowExceptionCombination(int seed);
}

internal interface IRandomCilInteractionGenerator
{
    GeneratedCilProgram Generate(
        RandomCilInteractionCase testCase,
        RandomCilMetadataTokens tokens);
}

internal interface IGeneratedCilValidator
{
    void Validate(GeneratedCilProgram program);

    int MeasureMaximumStackDepth(GeneratedCilProgram program);
}

internal interface IGeneratedCilSerializer
{
    byte[] Serialize(GeneratedCilProgram program);

    SerializedGeneratedMethod SerializeMethod(GeneratedCilProgram program);
}

internal sealed record SerializedGeneratedExceptionRegion(
    CilExceptionRegionKind Kind,
    int TryOffset,
    int TryLength,
    int HandlerOffset,
    int HandlerLength,
    int? FilterOffset,
    int? CatchTypeToken);

internal sealed record SerializedGeneratedMethod(
    byte[] Cil,
    ImmutableArray<SerializedGeneratedExceptionRegion> ExceptionRegions);

internal sealed record PatchedMethodBody(
    string AssemblyPath,
    string AssemblySha256,
    int MethodBodyCapacity,
    int GeneratedCodeSize);

internal interface IMethodBodyPatcher
{
    PatchedMethodBody Patch(
        string assemblyPath,
        string typeName,
        string methodName,
        ReadOnlySpan<byte> cil,
        int maxStack);

    PatchedMethodBody Patch(
        string assemblyPath,
        string typeName,
        string methodName,
        SerializedGeneratedMethod method,
        int maxStack);
}

internal sealed record RandomCilMetadataTokens(
    int BoxConstructor,
    int BoxValueField,
    int BoxAddMethod,
    int StaticValueField,
    int HelperMethod,
    int HelperUnaryMethod,
    int UnaryCallSite,
    int UserString,
    int GetHashCodeMethod,
    int Int32Type,
    int BoxType,
    int InterfaceAddMethod,
    int ArrayInitializerField,
    int InitializeArrayMethod,
    int StaticTransformType,
    int StaticApplyMethod,
    int DefaultBoxConstructor,
    int DefaultAddMethod,
    int DerivedBoxConstructor,
    int BaseCopyMethod,
    int BaseReadMethod,
    int ExceptionType,
    int RangeSliceMethod);

internal interface IRandomCilMetadataTokenResolver
{
    RandomCilMetadataTokens Resolve(string assemblyPath);
}
