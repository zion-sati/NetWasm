using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ObjectLayoutResolver(
    ITypeDefinitionResolver typeDefinitions,
    ManagedTypeLayouts layouts) : IObjectLayoutResolver
{
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly ManagedTypeLayouts _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));

    public ObjectLayout Resolve(EntityKey type) =>
        _layouts.Objects.TryGetValue(type, out var layout)
            ? layout
            : throw Missing(type);

    public ObjectLayout Resolve(CliTypeIdentity type)
    {
        if (_layouts.ConstructedObjects.TryGetValue(type, out var layout) ||
            _layouts.ObjectIdentities.TryGetValue(type, out layout))
        {
            return layout;
        }

        var definition = _typeDefinitions.ResolveTypeIdentity(type);
        return _layouts.Objects.TryGetValue(definition.Key, out layout)
            ? layout
            : throw Missing(type);
    }

    private static CompilerException Missing(object type) => new(new CompilerDiagnostic(
        DiagnosticCode.RuntimeContract,
        $"object layout for '{type}' was not generated"));
}
