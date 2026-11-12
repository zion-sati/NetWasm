using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

// A Main(string[]) receives the command line from CoreLib's argument factory, or, when its body never
// reads the parameter (top-level statements that ignore args), from the factory that reads nothing.
internal sealed class ManagedExecutableArgumentFactoryResolver(IArgumentReadDetector reads) :
    IManagedExecutableArgumentFactoryResolver
{
    private const string PlatformServicesType =
        "System.Runtime.InteropServices.PlatformServices";
    private const string ArgumentFactoryMethod = "ReadManagedEntryPointArguments";
    private const string IgnoredArgumentsMethod = "IgnoredManagedEntryPointArguments";

    private readonly IArgumentReadDetector _reads = reads ??
        throw new ArgumentNullException(nameof(reads));

    public EntityKey? Resolve(
        CompilerEntryPointKind kind,
        MethodDefinitionModel entryPoint,
        ITypeFinder types,
        IMethodRepository methods,
        IMethodBodyReader bodies)
    {
        ArgumentNullException.ThrowIfNull(entryPoint);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(bodies);

        if (kind != CompilerEntryPointKind.ManagedExecutable ||
            entryPoint.Signature.ParameterSignatureTypes.IsEmpty)
        {
            return null;
        }

        var name = _reads.Detect(bodies.ReadMethodBody(entryPoint), 0)
            ? ArgumentFactoryMethod
            : IgnoredArgumentsMethod;
        var platformServices = types.FindType(PlatformServicesType);
        var candidates = platformServices.Methods
            .Select(methods.GetMethod)
            .Where(method => IsArgumentFactory(method, name))
            .ToArray();
        if (candidates is not [var factory])
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.InvalidEntryPoint,
                $"managed executable Main(string[]) requires one parameterless " +
                $"'{PlatformServicesType}::{name}' argument factory"));
        }

        return factory.Key;
    }

    private static bool IsArgumentFactory(MethodDefinitionModel method, string name) =>
        method.Name == name &&
        method.IsStatic &&
        method.Signature.ParameterSignatureTypes.IsEmpty &&
        method.Signature.ReturnSignatureType is
        {
            Shape: CliTypeShape.SzArray,
            ElementType:
            {
                FullName: "System.String",
            }
            or
            {
                CanonicalName: "primitive:string",
            },
        };
}
