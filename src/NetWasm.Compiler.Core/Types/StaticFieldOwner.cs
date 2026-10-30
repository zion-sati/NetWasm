namespace NetWasm.Compiler.Core.Types;

// The type whose initialization a static field access checks: each
// instantiation of a generic type initializes on its own.
public sealed record StaticFieldOwner(EntityKey Type, CliTypeIdentity? Instantiation)
{
    public bool Initializes(StaticFieldOwner other) =>
        Type == other.Type &&
        (Instantiation?.Shape != CliTypeShape.GenericInstantiation &&
            other.Instantiation?.Shape != CliTypeShape.GenericInstantiation ||
            Instantiation is not null && Instantiation.Equals(other.Instantiation));
}
