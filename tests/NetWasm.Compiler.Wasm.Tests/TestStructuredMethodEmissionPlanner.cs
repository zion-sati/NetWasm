using System.Collections.Immutable;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

internal sealed class TestStructuredMethodEmissionPlanner(
    IManagedMethodIdentityFactory identities,
    ICilTypeIdentityResolver types) : IStructuredMethodEmissionPlanner
{
    public ImmutableArray<StructuredMethodEmission> Plan(WasmEmissionRequest request)
    {
        var definitions = request.Methods.Values
            .Select(method => new
            {
                Method = method,
                Definition = method.Header.Method,
                DeclaringType = types.Resolve(method.Header.Method.DeclaringType),
            })
            .Where(item =>
                item.Definition.GenericArity == 0 &&
                !item.DeclaringType.ContainsGenericParameters)
            .Select(item => new StructuredMethodEmission(
                identities.Create(item.Definition, item.DeclaringType),
                item.Method))
            .OrderBy(
                emission => emission.Identity.CanonicalName,
                StringComparer.Ordinal);

        var constructed = request.ConstructedMethods
            .Select(method => new StructuredMethodEmission(
                new ManagedMethodIdentity(method.Key),
                method.Value))
            .OrderBy(
                emission => emission.Identity.CanonicalName,
                StringComparer.Ordinal);

        return [.. definitions, .. constructed];
    }
}
