using System.Collections.Immutable;

namespace NetWasm.Compiler.Layout;

// One registered type of the runtime type table: what the GC, allocator, boxing and casting
// read at run time, and what CoreLib reads for its type queries.
internal sealed record RuntimeTypeTableEntry(
    int TypeId,
    uint Flags,
    int ObjectSize,
    ImmutableArray<int> ReferenceOffsets,
    int BaseTypeId,
    ImmutableArray<int> AssignableTypeIds)
{
    public bool HasFinalizer { get; init; }

    // A value type's unboxed layout, which a boxed value type has besides its object layout.
    public RuntimeValueLayout? Value { get; init; }

    // Generic arguments in declaration order, for a generic instantiation or definition.
    public ImmutableArray<int> GenericArgumentTypeIds { get; init; } = [];

    // A nested type's enclosing type or a generic parameter's owner (DeclaringType), or an array,
    // pointer or by-reference type's element (GetElementType); no type has both. 0 for none.
    public int RelatedTypeId { get; init; }

    // The open definition of a generic instantiation, or 0.
    public int GenericDefinitionTypeId { get; init; }

    // The index (from 1) of the assembly Type.Assembly reports, or 0.
    public int AssemblyIndex { get; init; }

    // The TypeAttributes Type.Attributes reports.
    public int TypeAttributes { get; init; }

    // The registered interfaces among AssignableTypeIds the type implements itself, which
    // GetInterfaces reports with its base types'; an interface reached only through variance is not one.
    public ImmutableArray<int> ImplementedInterfaceTypeIds { get; init; } = [];
}

internal sealed record RuntimeValueLayout(
    int Size,
    int BoxedPayloadOffset,
    ImmutableArray<int> ReferenceOffsets);

internal sealed record RuntimeTypeTableRequest(
    ImmutableArray<RuntimeTypeTableEntry> Entries,
    int ReferenceSize,
    bool IncludeGenericArguments,
    bool IncludeRelatedTypes,
    bool IncludeGenericDefinitions)
{
    // Whether the table holds the assembly runs.
    public bool IncludeAssemblies { get; init; }

    // The index of the assembly GetEntryAssembly reports, or 0.
    public int EntryAssemblyIndex { get; init; }

    // Whether each type's shape holds its TypeAttributes, as an index into the distinct values.
    public bool IncludeTypeAttributes { get; init; }

    // Whether a bit per extra assignable type says the type implements it.
    public bool IncludeInterfaces { get; init; }
}
