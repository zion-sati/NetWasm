using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal interface IRuntimeTypeTableBuilder
{
    void Build();
}

// Builder: the runtime type table of every registered type, from its descriptors, as one data
// segment that the runtime initializer registers and CoreLib's type queries read. CoreLib's flags,
// generic arguments, generic definitions, declaring types, element types, assemblies and
// TypeAttributes are there only when the program can ask for them. An assembly's index is its place among the registered types' assemblies
// in type ID order, which the type-symbols sidecar names it by.
internal sealed class RuntimeTypeTableBuilder(
    ReachableProgram program,
    WasmTargetLayout target,
    ManagedStaticDataBuildState state,
    ITypeIdentityResolver identities,
    IRuntimeTypeAssemblyResolver assemblies,
    IImplementedInterfaceResolver interfaces,
    IRuntimeTypeTableEncoder encoder) : IRuntimeTypeTableBuilder
{
    private const string GenericParameterPrefix = "generic-parameter:";
    private const int Public = 0x1;
    private static readonly HashSet<string> VectorInterfaces =
    [
        "System.Collections.Generic.IList`1", "System.Collections.Generic.ICollection`1",
        "System.Collections.Generic.IEnumerable`1", "System.Collections.Generic.IReadOnlyList`1",
        "System.Collections.Generic.IReadOnlyCollection`1",
    ];
    private const int PublicSealedSerializable = 0x2101;

    private readonly ReachableProgram _program = program ?? throw new ArgumentNullException(nameof(program));
    private readonly WasmTargetLayout _target = target ?? throw new ArgumentNullException(nameof(target));
    private readonly ManagedStaticDataBuildState _state = state ?? throw new ArgumentNullException(nameof(state));
    private readonly ITypeIdentityResolver _identities = identities ?? throw new ArgumentNullException(nameof(identities));
    private readonly IRuntimeTypeAssemblyResolver _assemblies = assemblies ?? throw new ArgumentNullException(nameof(assemblies));
    private readonly IImplementedInterfaceResolver _interfaces = interfaces ?? throw new ArgumentNullException(nameof(interfaces));
    private readonly IRuntimeTypeTableEncoder _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));

    public void Build()
    {
        var registrations = new Dictionary<int, Registration>();
        foreach (var descriptor in _state.TypeDescriptors)
        {
            Register(descriptor.TypeId, new(descriptor.BaseTypeId, descriptor.ObjectSize, descriptor.ReferenceOffsets,
                descriptor.AssignableTypeIds, descriptor.Finalizer is not null, descriptor.IsInterface));
        }
        foreach (var descriptor in _state.ConstructedTypeDescriptors)
        {
            Register(descriptor.TypeId, new(descriptor.BaseTypeId, descriptor.ObjectSize, descriptor.ReferenceOffsets,
                descriptor.AssignableTypeIds, descriptor.Finalizer is not null, descriptor.IsInterface));
        }
        // Metadata-only identities can become Type objects but are never allocated.
        foreach (var descriptor in _state.MetadataTypeDescriptors)
        {
            Register(descriptor.TypeId, new(0, 0, [], [], false, false));
        }
        var values = _state.ValueTypeDescriptors.ToDictionary(descriptor => descriptor.TypeId);
        var types = _state.PendingTypeFacts.ToDictionary(facts => facts.TypeId);
        var ids = _state.PendingTypeFacts.ToDictionary(facts => facts.Identity.CanonicalName, facts => facts.TypeId,
            StringComparer.Ordinal);
        if (!types.Keys.ToHashSet().SetEquals(registrations.Keys) || !values.Keys.All(registrations.ContainsKey))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                "the runtime type table does not describe every registered type"));
        }
        var assemblyIndexes = new Dictionary<AssemblyIdentity, int>();
        var entries = registrations.OrderBy(pair => pair.Key).Select(pair =>
        {
            var (typeId, registration) = pair;
            var type = types[typeId];
            var owner = type.Identity.Shape == CliTypeShape.GenericTypeParameter
                ? types[Registered(Owner(type), type, "owner")].Definition
                : null;
            var flags = _program.RequiresTypeFacts ? RuntimeTypeFactsEncoding.Word(type.Identity, type.Definition, owner) : 0;
            return new RuntimeTypeTableEntry(
                typeId,
                registration.IsInterface ? flags | (uint)RuntimeTypeFactsFlags.Interface : flags,
                registration.ObjectSize,
                registration.ReferenceOffsets,
                owner is null ? registration.BaseTypeId : ParameterBase(flags),
                registration.AssignableTypeIds)
            {
                HasFinalizer = registration.HasFinalizer,
                Value = values.TryGetValue(typeId, out var value)
                    ? new RuntimeValueLayout(value.Size, value.BoxedPayloadOffset, value.ReferenceOffsets)
                    : null,
                GenericArgumentTypeIds = type.GenericArgumentTypeIds,
                RelatedTypeId = Related(type),
                GenericDefinitionTypeId = _program.RequiresGenericDefinitions ? GenericDefinition(type) : 0,
                AssemblyIndex = _program.RequiresAssemblies ? AssemblyIndex(AssemblyOf(type)) : 0,
                TypeAttributes = _program.RequiresTypeAttributes ? AttributesOf(type) : 0,
                ImplementedInterfaceTypeIds = _program.RequiresInterfaces
                    ? Implemented(type, registration.AssignableTypeIds)
                    : [],
            };
        }).ToImmutableArray();
        var entryAssembly = _program.RequiresAssemblies && _program.EntryPoint is { } entryPoint
            ? AssemblyIndex(entryPoint.DeclaringType.Assembly)
            : 0;
        var table = _encoder.Encode(new(entries, _target.ObjectReferenceSize,
            _program.RequiresGenericArguments, _program.RequiresDeclaringTypes || _program.RequiresElementTypes,
            _program.RequiresGenericDefinitions)
        {
            IncludeAssemblies = _program.RequiresAssemblies,
            EntryAssemblyIndex = entryAssembly,
            IncludeTypeAttributes = _program.RequiresTypeAttributes,
            IncludeInterfaces = _program.RequiresInterfaces,
        });
        _state.RuntimeAssemblies = [.. assemblyIndexes.OrderBy(pair => pair.Value).Select(pair => pair.Key)];
        _state.TypeTableAddress = _state.Cursor;
        _state.Segments.Add(new DataSegment(_state.Cursor, table));
        _state.Cursor += table.Length;

        // The declaring type DeclaringType returns, or the element GetElementType returns, as far as
        // the program can ask for either; an array, pointer or by-reference type has no declaring type.
        int Related(PendingRuntimeTypeFacts type) =>
            _program.RequiresElementTypes && type.Identity.Shape is CliTypeShape.SzArray or CliTypeShape.Array or
                CliTypeShape.UnmanagedPointer or CliTypeShape.ManagedByReference
                ? Registered(type.Identity.ElementType!.CanonicalName, type, "element")
                : _program.RequiresDeclaringTypes ? Declaring(type) : 0;

        int Registered(string canonicalName, PendingRuntimeTypeFacts type, string role) =>
            ids.TryGetValue(canonicalName, out var id)
                ? id
                : throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.RuntimeContract,
                    $"the {role} of '{type.Identity}' is not registered"));

        // A nested type's enclosing definition, or a generic type parameter's owner, as .NET's
        // DeclaringType returns them.
        int Declaring(PendingRuntimeTypeFacts type)
        {
            var declaring = type.Identity.Shape switch
            {
                CliTypeShape.GenericTypeParameter => Owner(type),
                CliTypeShape.Named or CliTypeShape.GenericInstantiation
                    when type.Definition?.DeclaringType is { } key => _identities.GetTypeIdentity(key).CanonicalName,
                _ => null,
            };
            return declaring is null ? 0 : Registered(declaring, type, "declaring type");
        }

        // A generic instantiation's open definition.
        int GenericDefinition(PendingRuntimeTypeFacts type) => type.Identity.Shape != CliTypeShape.GenericInstantiation
            ? 0
            : Registered(type.Identity.ElementType!.CanonicalName, type, "generic definition");

        // The TypeAttributes .NET 11 reports: a definition's own, which its instantiations share; an
        // array is public, sealed and serializable; a pointer, by-reference, function pointer or
        // generic parameter is public.
        int AttributesOf(PendingRuntimeTypeFacts type) => type.Identity.Shape switch
        {
            CliTypeShape.SzArray or CliTypeShape.Array => PublicSealedSerializable,
            CliTypeShape.Named or CliTypeShape.Primitive or CliTypeShape.GenericInstantiation => type.Definition!.Attributes,
            _ => Public,
        };

        // The registered interfaces among a type's assignable types that it implements itself, as
        // GetInterfaces reports them: those its definition declares, and a vector's generic collection
        // interfaces of its own element; System.Array's come through the base chain.
        ImmutableArray<int> Implemented(PendingRuntimeTypeFacts type, ImmutableArray<int> assignable)
        {
            if (type.Identity.Shape == CliTypeShape.SzArray)
            {
                return [.. assignable.Where(id => types[id].Identity is { Shape: CliTypeShape.GenericInstantiation } target &&
                    VectorInterfaces.Contains(target.ElementType!.FullName!) && target.TypeArguments[0].Equals(type.Identity.ElementType))];
            }
            if (type.Identity.Shape is not (CliTypeShape.Named or CliTypeShape.Primitive or CliTypeShape.GenericInstantiation))
            {
                return [];
            }
            var declared = _interfaces.GetInterfaces(type.Identity).ToHashSet();
            return [.. assignable.Where(id => declared.Contains(types[id].Identity))];
        }

        // A generic parameter's base type as .NET reports it: ValueType under the struct constraint,
        // otherwise Object, where the program has that type.
        int ParameterBase(uint flags) => (flags & (uint)RuntimeTypeFactsFlags.ValueType) == 0
            ? ids.GetValueOrDefault("primitive:object")
            : types.Values.FirstOrDefault(facts =>
                facts.Identity.Shape == CliTypeShape.Named && facts.Identity.FullName == "System.ValueType")?.TypeId ?? 0;

        // A registered generic type parameter's owner, by canonical name.
        static string Owner(PendingRuntimeTypeFacts type) => type.Identity.CanonicalName[
            GenericParameterPrefix.Length..type.Identity.CanonicalName.LastIndexOf(':')];

        // The assembly Type.Assembly reports: a generic parameter's is its owner's.
        AssemblyIdentity AssemblyOf(PendingRuntimeTypeFacts type) =>
            type.Identity.Shape is CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter
                ? AssemblyOf(types[Registered(Owner(type), type, "owner")])
                : _assemblies.Resolve(type.Identity);

        int AssemblyIndex(AssemblyIdentity assembly)
        {
            if (!assemblyIndexes.TryGetValue(assembly, out var index))
            {
                index = assemblyIndexes.Count + 1;
                assemblyIndexes.Add(assembly, index);
            }
            return index;
        }

        void Register(int typeId, Registration registration)
        {
            if (!registrations.TryAdd(typeId, registration))
            {
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.RuntimeContract,
                    $"semantic type ID '{typeId}' is registered more than once"));
            }
        }
    }

    private sealed record Registration(
        int BaseTypeId,
        int ObjectSize,
        ImmutableArray<int> ReferenceOffsets,
        ImmutableArray<int> AssignableTypeIds,
        bool HasFinalizer,
        bool IsInterface);
}
