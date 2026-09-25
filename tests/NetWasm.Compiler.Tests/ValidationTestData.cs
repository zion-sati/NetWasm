using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structuring;
using StructuredMethod = NetWasm.Compiler.ControlFlow.Structured.StructuredMethod;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Tests;

internal static class ValidationTestData
{
    public static readonly AssemblyIdentity Assembly =
        new("ValidationUnitTests");

    public static ITypeRepository Types() => new UnitTypeRepository();

    public static IFieldRepository Fields() => new UnitFieldRepository();

    public static IMethodRepository Methods() => new UnitMethodRepository();

    public static ISymbolFormatter Symbols() => new UnitSymbolFormatter();

    public static IMethodInstanceResolver MethodInstances() => new UnitMethodInstanceResolver();

    public static ITypeClassifier TypeClassifier() => new UnitTypeClassifier();

    public static ReachableProgram Program()
    {
        var managed = ManagedMethodBody();
        var method = managed.Method.Definition;
        var instance = managed.Method;
        var type = method.DeclaringType;
        return new ReachableProgram(
            method,
            ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(
                method.Key,
                managed),
            [type],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [])
        {
            MethodInstances = ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                instance.CanonicalName,
                instance),
        };
    }

    public static ManagedMethodBody ManagedMethodBody() => ManagedMethodBody(
        [new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None())]);

    public static ManagedMethodBody ManagedMethodBody(
        IEnumerable<CilInstruction> instructions,
        MethodDefinitionModel? method = null,
        MethodInstanceModel? instance = null,
        ImmutableArray<CilExceptionRegion> exceptionRegions = default)
    {
        var validated = ValidatedControlFlow(
            instructions,
            method,
            instance,
            exceptionRegions,
            includeMethodInstance: true);
        return new ManagedMethodBody(validated.Graph.MethodBody.MethodInstance!, validated);
    }

    public static WasmMethodLoweringResult Lower(ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var lowerer = new WasmMethodLowerer(
            new ValidatedStructuredMethodBuilderFactory());
        return new(
            program.Methods.ToImmutableDictionary(
                pair => pair.Key,
                pair => lowerer.Lower(pair.Value)),
            program.ConstructedMethods.ToImmutableDictionary(
                pair => pair.Key,
                pair => lowerer.Lower(pair.Value),
                StringComparer.Ordinal));
    }

    public static StructuredMethod StructuredMethod() => StructuredMethod(
        [new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None())]);

    public static StructuredMethod StructuredMethod(
        IEnumerable<CilInstruction> instructions,
        MethodDefinitionModel? method = null,
        MethodInstanceModel? instance = null,
        ImmutableArray<CilExceptionRegion> exceptionRegions = default,
        bool includeMethodInstance = true)
    {
        var validated = ValidatedControlFlow(
            instructions,
            method,
            instance,
            exceptionRegions,
            includeMethodInstance);
        return new ValidatedStructuredMethodBuilderFactory()
            .Create()
            .Build(validated);
    }

    private static ValidatedControlFlowGraph ValidatedControlFlow(
        IEnumerable<CilInstruction> instructions,
        MethodDefinitionModel? method,
        MethodInstanceModel? instance,
        ImmutableArray<CilExceptionRegion> exceptionRegions,
        bool includeMethodInstance)
    {
        var type = new EntityKey(Assembly, 0x02000001);
        method ??= instance?.Definition ?? new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000001),
            type,
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        instance ??= new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "", "Unit", false),
            [],
            method.Signature);
        var body = new CilMethodBody(
            method,
            0,
            [],
            [.. instructions])
        {
            ExceptionRegions = exceptionRegions.IsDefault ? [] : exceptionRegions,
        };
        if (includeMethodInstance)
            body = body with { MethodInstance = instance };
        var graph = ((IControlFlowGraphBuilderFactory)new ControlFlowGraphBuilderFactory())
            .Create()
            .Build(body);
        var validated = new ValidatedControlFlowGraph(
            graph,
            graph.Blocks.ToImmutableDictionary(
                block => block.Index,
                _ => ImmutableArray<CliValueKind>.Empty),
            graph.Blocks.SelectMany(block => block.Instructions)
                .ToImmutableDictionary(
                    instruction => instruction.Offset,
                    _ => ImmutableArray<CliValueKind>.Empty));
        return validated;
    }

    private sealed class UnitTypeRepository : ITypeRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => Type();
    }

    private sealed class UnitMethodRepository : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => Method();
    }

    private sealed class UnitFieldRepository : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => new(
            key,
            new EntityKey(Assembly, 0x02000001),
            "Field",
            CliValueKind.I4,
            true);
    }

    private sealed class UnitSymbolFormatter : ISymbolFormatter
    {
        public string Format(EntityKey key) => key.ToString();

        public string Format(MethodDefinitionModel method) =>
            $"{method.DeclaringType}::{method.Name}";
    }

    private sealed class UnitMethodInstanceResolver : IMethodInstanceResolver
    {
        public MethodInstanceModel ResolveMethodInstance(
            AssemblyIdentity source,
            int metadataToken,
            string methodDisplayName,
            int ilOffset,
            CliGenericContext? genericContext = null) => Instance();
    }

    private sealed class UnitTypeClassifier : ITypeClassifier
    {
        public bool IsDelegateType(EntityKey type) => false;
    }

    public static TypeDefinitionModel Type() => new(
            new EntityKey(Assembly, 0x02000001),
            "",
            "Unit",
            false,
            [],
            [new EntityKey(Assembly, 0x06000001)]);

    public static MethodDefinitionModel Method() => new(
            new EntityKey(Assembly, 0x06000001),
            new EntityKey(Assembly, 0x02000001),
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);

    public static MethodInstanceModel Instance() => new(
        Method(),
        CliTypeIdentity.Named(Assembly, "", "Unit", false),
        [],
        Method().Signature);
}
