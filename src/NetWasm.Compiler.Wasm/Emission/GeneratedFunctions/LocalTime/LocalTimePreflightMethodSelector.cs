using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.LocalTime;

internal sealed class LocalTimePreflightMethodSelector(
    ITypeRepository types,
    IMethodRepository methods) : ILocalTimePreflightMethodSelector
{
    private const string PlatformNamespace = "System.Runtime.InteropServices";
    private const string PlatformType = "PlatformServices";
    private const string PreflightMethod = "EnsureLocalTimeReady";

    private readonly ITypeRepository _types =
        types ?? throw new ArgumentNullException(nameof(types));
    private readonly IMethodRepository _methods =
        methods ?? throw new ArgumentNullException(nameof(methods));

    public EntityKey? Select(WasmEmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (var key in request.Methods.Keys)
        {
            var method = _methods.GetMethod(key);
            if (!IsPreflightSignature(method))
            {
                continue;
            }

            var type = _types.GetTypeDefinition(method.DeclaringType);
            if (type.Namespace == PlatformNamespace && type.Name == PlatformType)
            {
                return key;
            }
        }

        return null;
    }

    private static bool IsPreflightSignature(MethodDefinitionModel method) =>
        method.Name == PreflightMethod &&
        method.IsStatic &&
        method.Signature.ReturnType == CliValueKind.Void &&
        method.Signature.ParameterTypes.IsEmpty;
}
