using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core;

public enum DiagnosticCode
{
    InvalidCommandLine = 1000,
    UnsupportedCil = 1001,
    InvalidCil = 1002,
    UnsupportedMetadata = 1003,
    AssemblyResolution = 1004,
    DuplicateAssembly = 1005,
    InvalidEntryPoint = 1006,
    IrreducibleControlFlow = 1007,
    RuntimeContract = 1008,
    ComponentContract = 1009,
    ComponentToolchain = 1010,
    CompilerInvariant = 1011,
    NativeInterop = 1012,
    // A function past a limit engines put on one function: body size, locals or parameters.
    FunctionLimit = 1013,
    // A host could not read or write a file, or failed unexpectedly.
    HostFailure = 1014,
    // A program's NetWasmPlatformAttribute declaration is invalid.
    PlatformService = 1015,
    // A resolved type has no member matching a reference's name, signature or shape.
    MissingApi = 1016,
    // A warning: a reached P/Invoke names a native library the application does not link, so it throws
    // DllNotFoundException when called.
    UnlinkedNativeLibrary = 1017,
    GenericExpansion = 2001,
}

/// <summary>The metadata source requiring an operation, with its closed identity when known.</summary>
public sealed record CompilerDiagnosticContext(EntityKey Source, string? InstanceIdentity = null)
{
    public CliTypeIdentity? DeclaringType { get; init; }
}

public enum CompilerDependencyKind { TypeStructure, BaseType, Interface, TypeArgument, MethodDependency, FieldStorage }

/// <summary>A requiring source and the dependency it introduced.</summary>
public sealed record CompilerDependencyStep(CompilerDiagnosticContext Source, CompilerDependencyKind Kind);

public sealed record CompilerDiagnostic(
    DiagnosticCode Code,
    string Message,
    string? Method = null,
    int? IlOffset = null)
{
    public CompilerDiagnosticContext? Context { get; init; }
    public CompilationReferenceOrigin? Origin { get; init; }
    public ImmutableArray<CompilerDependencyStep> DependencyChain { get; init; } = [];

    public string Id => Code == DiagnosticCode.GenericExpansion
        ? "NWA2001"
        : $"NW{(int)Code:0000}";

    public override string ToString()
    {
        var location = Method is null
            ? string.Empty
            : IlOffset is null
                ? $"{Method}: "
                : $"{Method} IL_{IlOffset.Value:x4}: ";
        var origin = Origin is { PackageId: { Length: > 0 } packageId } input
            ? $" [package {packageId}" +
                (string.IsNullOrEmpty(input.PackageVersion) ? "" : $" {input.PackageVersion}") +
                (string.IsNullOrEmpty(input.AssetPath) ? "" : $"; {input.AssetPath}") + "]"
            : string.Empty;
        var chain = DependencyChain.IsDefaultOrEmpty
            ? string.Empty
            : " [required through " + string.Join(" -> ", DependencyChain.Select(step =>
                (step.Source.InstanceIdentity ?? step.Source.Source.ToString()) + " (" + step.Kind + ")")) + "]";
        return $"{Id}: {location}{Message}{origin}{chain}";
    }
}

public sealed class CompilerException : Exception
{
    public CompilerException(CompilerDiagnostic diagnostic) : this(diagnostic, null)
    {
    }

    public CompilerException(CompilerDiagnostic diagnostic, Exception? innerException)
        : base(diagnostic.ToString(), innerException)
    {
        Diagnostic = diagnostic;
    }

    public CompilerDiagnostic Diagnostic { get; }
}
