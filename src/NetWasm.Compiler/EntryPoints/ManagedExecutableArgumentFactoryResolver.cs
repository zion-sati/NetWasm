using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal sealed class ManagedExecutableArgumentFactoryResolver :
    IManagedExecutableArgumentFactoryResolver
{
    private const string PlatformServicesType =
        "System.Runtime.InteropServices.PlatformServices";
    private const string ArgumentFactoryMethod = "ReadCommandLineArguments";

    public EntityKey? Resolve(
        CompilerEntryPointKind kind,
        MethodDefinitionModel entryPoint,
        ITypeFinder types,
        IMethodRepository methods)
    {
        ArgumentNullException.ThrowIfNull(entryPoint);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(methods);

        if (kind != CompilerEntryPointKind.ManagedExecutable ||
            entryPoint.Signature.ParameterSignatureTypes.IsEmpty)
        {
            return null;
        }

        var platformServices = types.FindType(PlatformServicesType);
        var candidates = platformServices.Methods
            .Select(methods.GetMethod)
            .Where(IsArgumentFactory)
            .ToArray();
        if (candidates is not [var factory])
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.InvalidEntryPoint,
                $"managed executable Main(string[]) requires one parameterless " +
                $"'{PlatformServicesType}::{ArgumentFactoryMethod}' argument factory"));
        }

        return factory.Key;
    }

    private static bool IsArgumentFactory(MethodDefinitionModel method) =>
        method.Name == ArgumentFactoryMethod &&
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
