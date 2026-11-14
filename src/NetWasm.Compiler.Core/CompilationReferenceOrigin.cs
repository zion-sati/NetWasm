namespace NetWasm.Compiler.Core;

public enum CompilationReferenceRole
{
    Implementation,
    Metadata,
}

/// <summary>Host-supplied provenance for one physical compilation input.</summary>
public sealed record CompilationReferenceOrigin(
    string Path,
    CompilationReferenceRole Role,
    string? PackageId = null,
    string? PackageVersion = null,
    string? AssetPath = null,
    string? OriginalReferencePath = null);
