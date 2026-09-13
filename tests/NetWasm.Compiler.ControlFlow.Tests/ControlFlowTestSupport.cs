using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NwDraft = global::NetWasm.Compiler.ControlFlow.Draft;
using Final = global::NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ControlFlow.Structuring;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests;

internal static class ControlFlowTestSupport
{
    internal static readonly AssemblyIdentity Assembly = new("Test");
    internal static readonly EntityKey TypeKey = Key(0x02000001);
    internal static readonly EntityKey InstanceFieldKey = Key(0x04000001);
    internal static readonly EntityKey StaticFieldKey = Key(0x04000002);
    internal static readonly EntityKey StaticCallKey = Key(0x06000002);
    internal static readonly EntityKey InstanceCallKey = Key(0x06000003);
    internal static readonly EntityKey ConstructorKey = Key(0x06000004);
    internal static readonly EntityKey NonVirtualCallKey = Key(0x06000005);
    internal static readonly EntityKey ValueTypeKey = Key(0x02000002);
    internal static readonly EntityKey ValueInstanceCallKey = Key(0x06000006);
    internal static readonly EntityKey ValueConstructorKey = Key(0x06000007);
    internal static readonly EntityKey NoArgumentStaticCallKey = Key(0x06000008);
    internal static readonly EntityKey ManagedAddressCallKey = Key(0x06000009);
    internal static readonly EntityKey IntPtrTypeKey = Key(0x02000003);
    internal static readonly EntityKey UIntPtrTypeKey = Key(0x02000004);
    internal static readonly EntityKey IntPtrConstructorKey = Key(0x0600000A);
    internal static readonly EntityKey UIntPtrConstructorKey = Key(0x0600000B);
    internal static readonly EntityKey NativeIntCallKey = Key(0x0600000C);
    internal static ValidatedControlFlowGraph Validate(CilMethodBody body)
    {
        var graph = CreateGraphBuilder().Build(body);
        var program = new FakeProgram();
        return CreateValidator(program).Validate(graph);
    }

    internal static IControlFlowGraphBuilder CreateGraphBuilder() =>
        ((IControlFlowGraphBuilderFactory)new ControlFlowGraphBuilderFactory(
            CreateExceptionRegionValidator())).Create();

    internal static ICilExceptionRegionValidator CreateExceptionRegionValidator() =>
        new CilExceptionRegionValidator();

    internal static IControlFlowGraphAnalyzer CreateGraphAnalyzer() =>
        ((IControlFlowGraphAnalyzerFactory)new ControlFlowGraphAnalyzerFactory(
            CreateDominanceAnalyzer(),
            new ControlFlowPostDominanceAnalyzer(),
            new ControlFlowComponentAnalyzer(),
            CreateNaturalLoopAnalyzer())).Create();

    internal static IControlFlowDominanceAnalyzer CreateDominanceAnalyzer() =>
        new ControlFlowDominanceAnalyzer();

    internal static IControlFlowPostDominanceAnalyzer CreatePostDominanceAnalyzer() =>
        new ControlFlowPostDominanceAnalyzer();

    internal static IControlFlowComponentAnalyzer CreateComponentAnalyzer() =>
        new ControlFlowComponentAnalyzer();

    internal static IControlFlowNaturalLoopAnalyzer CreateNaturalLoopAnalyzer() =>
        new ControlFlowNaturalLoopAnalyzer(CreateDominanceAnalyzer());

    internal static ITypedStackValidator CreateValidator(FakeProgram program) =>
        ((ITypedStackValidatorFactory)new TypedStackValidatorFactory(
            new StackTypeCompatibilityValidator()))
            .Create(program, program, program);

    internal static Final.StructuredMethod Structurize(CilMethodBody body) =>
        new ValidatedStructuredMethodBuilderFactory().Create().Build(Validate(body));

    internal static NwDraft.StructuredMethodDraft DraftMethod(CilMethodBody body)
    {
        var validated = Validate(body);
        var entry = validated.Graph.Entry;
        return new NwDraft.StructuredMethodDraft(
            validated,
            new NwDraft.StructuredSequenceDraft([new NwDraft.StructuredBlockDraft(entry)]),
            [],
            []);
    }

    internal static CilMethodBody Body(
        CliValueKind result,
        int maxStack,
        ImmutableArray<CliValueKind> locals,
        params CilInstruction[] instructions)
    {
        var method = new MethodDefinitionModel(
            Key(0x06000001),
            TypeKey,
            "Run",
            true,
            MethodSignatureModel.Create(result, CliValueKind.I4),
            1);
        return new CilMethodBody(method, maxStack, locals, [.. instructions]);
    }

    internal static CilInstruction I(
        int offset,
        CilOperation operation,
        CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    internal static EntityKey Key(int token) => new(Assembly, token);

    internal static void AssertDiagnostic(Action action, string message)
    {
        var exception = Assert.Throws<CompilerException>(action);
        Assert.Equal(DiagnosticCode.InvalidCil, exception.Diagnostic.Code);
        Assert.Contains(message, exception.Message);
    }

}

internal static class ControlFlowGraphBuilder
{
    internal static ControlFlowGraph Build(CilMethodBody methodBody) =>
        ControlFlowTestSupport.CreateGraphBuilder().Build(methodBody);
}

internal sealed class FakeProgram :
    ITypeRepository,
    IFieldRepository,
    IMethodRepository,
    ISymbolFormatter,
    ITypeClassifier
{
    public bool IsDelegateType(EntityKey type) => false;

    private readonly Dictionary<EntityKey, MethodDefinitionModel> _methods = new()
    {
        [StaticCallKey] = Method(StaticCallKey, "Static", true, CliValueKind.I4, CliValueKind.I4),
        [InstanceCallKey] = Method(InstanceCallKey, "Instance", false, CliValueKind.I4, CliValueKind.I4),
        [ConstructorKey] = Method(ConstructorKey, ".ctor", false, CliValueKind.Void, CliValueKind.I4),
        [NonVirtualCallKey] = Method(NonVirtualCallKey, "NonVirtual", false, CliValueKind.I4),
        [ValueInstanceCallKey] = Method(
            ValueInstanceCallKey,
            "ValueInstance",
            false,
            CliValueKind.I4,
            ValueTypeKey),
        [ValueConstructorKey] = Method(
            ValueConstructorKey,
            ".ctor",
            false,
            CliValueKind.Void,
            ValueTypeKey,
            CliValueKind.I4),
        [NoArgumentStaticCallKey] = Method(
            NoArgumentStaticCallKey,
            "NoArgumentStatic",
            true,
            CliValueKind.I4),
        [ManagedAddressCallKey] = Method(
            ManagedAddressCallKey,
            "ManagedAddress",
            true,
            CliValueKind.I4,
            CliValueKind.ManagedAddress),
        [IntPtrConstructorKey] = Method(
            IntPtrConstructorKey,
            ".ctor",
            false,
            CliValueKind.Void,
            IntPtrTypeKey,
            CliValueKind.I4),
        [UIntPtrConstructorKey] = Method(
            UIntPtrConstructorKey,
            ".ctor",
            false,
            CliValueKind.Void,
            UIntPtrTypeKey,
            CliValueKind.I4),
        [NativeIntCallKey] = Method(
            NativeIntCallKey,
            "ConsumeNativeInt",
            true,
            CliValueKind.Void,
            CliValueKind.NativeInt),
    };

    public TypeDefinitionModel GetTypeDefinition(EntityKey key)
    {
        var (typeNamespace, name, isValueType) = key switch
        {
            var value when value == ValueTypeKey => ("Test", "Value", true),
            var value when value == IntPtrTypeKey => ("System", "IntPtr", true),
            var value when value == UIntPtrTypeKey => ("System", "UIntPtr", true),
            _ => ("Test", "Type", false),
        };
        return new(
            key,
            typeNamespace,
            name,
            isValueType,
            [InstanceFieldKey, StaticFieldKey],
            [.. _methods.Keys]);
    }

    public FieldDefinitionModel GetField(EntityKey key) => key == InstanceFieldKey
        ? new(key, TypeKey, "Instance", CliValueKind.I4, false)
        : new(key, TypeKey, "Static", CliValueKind.I4, true);

    public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];

    public string Format(EntityKey key) => "Test.Type";

    public string Format(MethodDefinitionModel method) =>
        "Test.Type::" + method.Name;

    private static MethodDefinitionModel Method(
        EntityKey key,
        string name,
        bool isStatic,
        CliValueKind result,
        params CliValueKind[] parameters) => CreateMethod(
            key,
            TypeKey,
            name,
            isStatic,
            result,
            parameters);

    private static MethodDefinitionModel Method(
        EntityKey key,
        string name,
        bool isStatic,
        CliValueKind result,
        EntityKey declaringType,
        params CliValueKind[] parameters) => CreateMethod(
            key,
            declaringType,
            name,
            isStatic,
            result,
            parameters);

    private static MethodDefinitionModel CreateMethod(
        EntityKey key,
        EntityKey declaringType,
        string name,
        bool isStatic,
        CliValueKind result,
        CliValueKind[] parameters) => new(
            key,
            declaringType,
            name,
            isStatic,
        MethodSignatureModel.Create(result, parameters),
        1)
        {
            IsVirtual = key == InstanceCallKey,
        };
}
