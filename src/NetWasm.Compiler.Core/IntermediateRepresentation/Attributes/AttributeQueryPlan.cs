using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

public enum AttributeArgumentKind
{
    Null,
    Boolean,
    Character,
    SignedInteger,
    UnsignedInteger,
    Floating32,
    Floating64,
    Text,
    Type,
    Array,
}

public sealed record AttributeArgumentPlan(
    CliTypeIdentity Type,
    AttributeArgumentKind Kind)
{
    // Object-typed parameters preserve the concrete serialized type for boxing
    // and array construction. Type remains the destination signature type.
    public CliTypeIdentity? SerializedType { get; init; }

    public long SignedValue { get; init; }

    public ulong UnsignedValue { get; init; }

    public double FloatingValue { get; init; }

    public string? StringValue { get; init; }

    public CliTypeIdentity? TypeValue { get; init; }

    public ImmutableArray<AttributeArgumentPlan> Elements { get; init; } = [];
}

public sealed record AttributeNamedArgumentPlan(
    string Name,
    AttributeArgumentPlan Value)
{
    public FieldInstanceModel? Field { get; init; }

    public MethodInstanceModel? Setter { get; init; }
}

public sealed record AttributeConstructionPlan(
    CliTypeIdentity AttributeType,
    MethodInstanceModel Constructor,
    ImmutableArray<AttributeArgumentPlan> ConstructorArguments,
    ImmutableArray<AttributeNamedArgumentPlan> NamedArguments);

public sealed record AttributeQueryEntry(
    CliTypeIdentity TargetType,
    CliTypeIdentity FilterType,
    AttributeQueryResult Direct,
    AttributeQueryResult Inherited);

public abstract record AttributeQueryResult;

public sealed record AttributeExistenceResult(bool IsDefined) : AttributeQueryResult;

public sealed record AttributeRetrievalResult(ImmutableArray<AttributeConstructionPlan> Attributes) : AttributeQueryResult;
