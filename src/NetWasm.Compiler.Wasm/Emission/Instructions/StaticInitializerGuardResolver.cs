using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class StaticInitializerGuardResolver(
    ITypeRepository types,
    IMethodRepository methods) : IStaticInitializerGuardResolver
{
    public bool TryResolve(StaticInitializationEmissionRequest request, [NotNullWhen(true)] out StaticInitializerGuard? guard)
    {
        guard = null;
        var definition = types.GetTypeDefinition(request.TypeDefinition);
        // BeforeFieldInit owners initialize on field access, even when another
        // reachable field access has already caused their guard to be planned.
        if (request.IsMethodInvocation && definition.IsBeforeFieldInit) return false;
        var initializer = definition.Methods
            .Select(methods.GetMethod).SingleOrDefault(method => method.Name == ".cctor");
        if (initializer is null) return false;
        var key = request.DeclaringType?.Shape == CliTypeShape.GenericInstantiation
            ? $"{request.DeclaringType.CanonicalName}::0x{initializer.Key.MetadataToken:x8}"
            : StaticInitializerGuard.KeyFor(initializer.Key);
        return request.Guards.TryGetValue(key, out guard);
    }
}
