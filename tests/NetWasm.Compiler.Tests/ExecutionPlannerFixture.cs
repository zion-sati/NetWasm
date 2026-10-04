using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Delegates;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

internal sealed class ExecutionPlannerFixture :
    IRuntimeIntrinsicRegistry,
    IDelegateTypeRecognizer,
    ITypeFinder,
    ITypeDefinitionResolver,
    IMethodRepository,
    IMethodInstanceResolver,
    ISymbolFormatter,
    INullableTypeResolver
{
    internal static readonly AssemblyIdentity Assembly = new("ExecutionPlannerTests");
    internal static readonly CliTypeIdentity Scalar = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    internal readonly Dictionary<EntityKey, RuntimeIntrinsic> Intrinsics = [];
    internal readonly HashSet<CliTypeIdentity> Delegates = [];
    internal readonly Dictionary<string, TypeDefinitionModel> Types = [];
    internal readonly Dictionary<CliTypeIdentity, TypeDefinitionModel> Definitions = [];
    internal readonly Dictionary<CliTypeIdentity, CliTypeIdentity> NullableTypes = [];
    internal readonly Dictionary<EntityKey, MethodInstanceModel> Methods = [];
    internal readonly List<string> Lookups = [];
    internal readonly List<(EntityKey Key, CliGenericContext? Context)> Resolutions = [];
    internal readonly List<CliTypeIdentity> Recognitions = [];
    internal readonly List<CliTypeIdentity> DefinitionLookups = [];

    internal IObjectArrayDelegateAdapterPlanner AdapterPlanner() => new ObjectArrayDelegateAdapterPlanner(
        this, this, this, this, this, this, this);

    internal IMemberExecutionPlanner MemberPlanner() => new MemberExecutionPlanner(
        this, this, this, this, this, this, this);

    public CliTypeIdentity? Resolve(CliTypeIdentity type) => NullableTypes.GetValueOrDefault(type);

    internal static CliTypeIdentity Type(string name, bool valueType = false) =>
        CliTypeIdentity.Named(Assembly, "Fixture", name, valueType);

    internal MethodInstanceModel Method(
        int row,
        string name,
        CliTypeIdentity? owner = null,
        CliTypeIdentity? result = null,
        bool isStatic = false,
        bool isAbstract = false,
        bool isVirtual = false,
        int genericArity = 0,
        params CliTypeIdentity[] parameters)
    {
        var definition = new MethodDefinitionModel(
            new(Assembly, 0x06000000 + row),
            new(Assembly, 0x02000001),
            name,
            isStatic,
            MethodSignatureModel.Create(result ?? Scalar, parameters),
            1)
        {
            GenericArity = genericArity,
            IsAbstract = isAbstract,
            IsVirtual = isVirtual,
        };
        var method = new MethodInstanceModel(definition, owner ?? Type("Owner"), [], definition.Signature);
        Methods.Add(definition.Key, method);
        return method;
    }

    internal MethodInstanceModel Demand(int row, RuntimeIntrinsic intrinsic)
    {
        var method = Method(row, "Demand", isStatic: true);
        Intrinsics.Add(method.Definition.Key, intrinsic);
        return method;
    }

    internal MethodInstanceModel Factory(CliTypeIdentity delegateType) =>
        Demand(1, RuntimeIntrinsic.ObjectArrayDelegateAdapterCreate) with { MethodArguments = [delegateType] };

    internal TypeDefinitionModel RegisterType(string fullName, params MethodInstanceModel[] methods)
    {
        var separator = fullName.LastIndexOf('.');
        var definition = new TypeDefinitionModel(
            new(Assembly, 0x02000000 + Types.Count + 1),
            fullName[..separator],
            fullName[(separator + 1)..],
            false,
            [],
            [.. methods.Select(method => method.Definition.Key)]);
        Types[fullName] = definition;
        return definition;
    }

    internal void RegisterDelegate(CliTypeIdentity type, params MethodInstanceModel[] methods)
    {
        Delegates.Add(type);
        Definitions[type] = RegisterType(type.FullName ?? "Fixture.GenericDelegate", methods);
    }

    internal MethodInstanceModel UnarySupport()
    {
        var method = Method(100, "Invoke1", genericArity: 2, parameters: [Scalar]);
        RegisterType("System.Runtime.CompilerServices.ObjectArrayDelegateTarget", method);
        return method;
    }

    internal MethodInstanceModel UnsupportedSupport(bool member = false)
    {
        var method = Method(101, "ThrowUnsupported", isStatic: true);
        RegisterType(member
            ? "System.Runtime.CompilerServices.RuntimeMemberExecution"
            : "System.Runtime.CompilerServices.ObjectArrayDelegateAdapter", method);
        return method;
    }

    internal MethodInstanceModel InvalidDelegateSupport()
    {
        var method = Method(102, "ThrowInvalidDelegate", isStatic: true);
        RegisterType(
            "System.Runtime.CompilerServices.ObjectArrayDelegateAdapter",
            method);
        return method;
    }

    public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic) =>
        Intrinsics.TryGetValue(method, out intrinsic);

    public bool Recognize(CliTypeIdentity type)
    {
        Recognitions.Add(type);
        return Delegates.Contains(type);
    }

    public TypeDefinitionModel FindType(string fullName)
    {
        Lookups.Add(fullName);
        return Types[fullName];
    }

    public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
    {
        DefinitionLookups.Add(identity);
        return Definitions[identity];
    }

    public MethodDefinitionModel GetMethod(EntityKey key) => Methods[key].Definition;

    public MethodInstanceModel ResolveMethodInstance(
        AssemblyIdentity source,
        int metadataToken,
        string methodDisplayName,
        int ilOffset,
        CliGenericContext? genericContext = null)
    {
        var key = new EntityKey(source, metadataToken);
        Resolutions.Add((key, genericContext));
        return Methods[key];
    }

    public string Format(EntityKey key) => key.ToString();
    public string Format(MethodDefinitionModel method) => method.Name;
}
