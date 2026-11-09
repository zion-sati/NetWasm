using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal sealed class ManagedExecutableExitResolver : IManagedExecutableExitResolver
{
    private const string PlatformServicesType =
        "System.Runtime.InteropServices.PlatformServices";
    private const string ExitMethod = "RunProcessExit";

    public EntityKey? Resolve(CompilerEntryPointKind kind, ITypeFinder types, IMethodRepository methods)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(methods);
        if (kind != CompilerEntryPointKind.ManagedExecutable)
        {
            return null;
        }

        var candidates = types.FindType(PlatformServicesType).Methods
            .Select(methods.GetMethod)
            .Where(method => method.Name == ExitMethod && method.IsStatic &&
                method.Signature.ParameterSignatureTypes.IsEmpty &&
                method.Signature.ReturnType == CliValueKind.Void)
            .ToArray();
        return candidates is [var exit]
            ? exit.Key
            : throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.InvalidEntryPoint,
                $"a managed executable requires one parameterless '{PlatformServicesType}::{ExitMethod}' " +
                "process-exit method"));
    }
}
