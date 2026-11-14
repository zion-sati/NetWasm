namespace NetWasm.Compiler.Core.UnsafeAccessors;

// Keep malformed declarations as metadata facts. Only a reachable accessor
// needs a generated body (including desktop-defined throwing bodies).
public sealed record UnsafeAccessorDeclaration(
    int Kind,
    string? Name,
    bool NameSpecified,
    bool IsMalformed,
    UnsafeAccessorTypeTranslations? TypeTranslations = null)
{
    public bool HasTypeTranslation => TypeTranslations is not null;
}

public enum UnsafeAccessorMemberKind
{
    Constructor,
    Method,
    StaticMethod,
    Field,
    StaticField,
}
