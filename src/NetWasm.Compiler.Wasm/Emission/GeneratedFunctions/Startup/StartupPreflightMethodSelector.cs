using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.Startup;

// The CoreLib preflights a program runs before its own code, in order, each only when reachable: loading the
// symbols sidecars (rooted by NetWasmTypeNames or NetWasmManagedStackTrace), so that names and frames are ready
// for anything that follows, then local time. A module without a process entry runs them when it initializes,
// in the task of the first export called, and a synchronous WIT export's task may never wait, while reading a
// file waits on wasi:filesystem: a module with synchronous WIT exports skips the symbols files and reads type
// and method IDs. A JavaScript host's raw module still loads them, as its host completes the read or answers
// that it cannot.
internal sealed class StartupPreflightMethodSelector(
    ITypeRepository types,
    IMethodRepository methods) : IStartupPreflightMethodSelector
{
    private static readonly ImmutableArray<(string Namespace, string Type, string Method)> Preflights =
    [
        ("System.Runtime.InteropServices", "ProgramSidecars", "LoadTypeSymbols"),
        ("System.Runtime.InteropServices", "ProgramSidecars", "LoadMethodSymbols"),
        ("System.Runtime.InteropServices", "PlatformServices", "EnsureLocalTimeReady"),
    ];
    private const int LocalTimeIndex = 2;

    private readonly ITypeRepository _types =
        types ?? throw new ArgumentNullException(nameof(types));
    private readonly IMethodRepository _methods =
        methods ?? throw new ArgumentNullException(nameof(methods));

    public StartupPreflights Select(WasmEmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loadsSymbols = StartupPreflightPlacement.RunsInProcessEntry(request) || request.UseJavaScriptExportBoundary ||
            !request.ComponentContract.Exports.Any(static export =>
                export.Kind is CanonicalAbiFunctionKind.Function or CanonicalAbiFunctionKind.ExportedResourceDestructor);
        var found = new EntityKey?[Preflights.Length];
        foreach (var key in request.Methods.Keys)
        {
            var method = _methods.GetMethod(key);
            if (!IsPreflightSignature(method))
            {
                continue;
            }

            var type = _types.GetTypeDefinition(method.DeclaringType);
            var index = Preflights.IndexOf((type.Namespace, type.Name, method.Name));
            if (index == LocalTimeIndex || index >= 0 && loadsSymbols)
            {
                found[index] = key;
            }
        }

        var selected = ImmutableArray.CreateBuilder<EntityKey>();
        foreach (var key in found)
        {
            if (key is { } preflight)
            {
                selected.Add(preflight);
            }
        }
        return new(selected.ToImmutable(), found[LocalTimeIndex] is not null);
    }

    private static bool IsPreflightSignature(MethodDefinitionModel method) =>
        method.IsStatic &&
        method.Signature.ReturnType == CliValueKind.Void &&
        method.Signature.ParameterTypes.IsEmpty;
}
