using System;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class TypeLayoutProvider(
    ITypeDefinitionResolver types,
    ManagedLayoutSnapshot snapshot) : ITypeLayoutProvider
{
    private readonly ITypeDefinitionResolver _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly ManagedLayoutSnapshot _snapshot = snapshot ??
        throw new ArgumentNullException(nameof(snapshot));

    public int ReferenceArrayTypeId => _snapshot.ReferenceArrayTypeId;
    public int StringTypeId => _snapshot.StringTypeId;
    public int TypeTypeId => _snapshot.TypeTypeId;

    public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout)
    {
        try
        {
            layout = GetObjectLayout(type);
            return true;
        }
        catch (CompilerException)
        {
            layout = default;
            return false;
        }
    }

    public ObjectLayout GetObjectLayout(EntityKey type) =>
        _snapshot.TypeLayouts.Objects.TryGetValue(type, out var layout)
            ? layout
            : throw Missing(type);

    public ObjectLayout GetObjectLayout(CliTypeIdentity type) =>
        _snapshot.TypeLayouts.ConstructedObjects.TryGetValue(type, out var layout)
            ? layout
            : type.Shape == CliTypeShape.Primitive
                ? GetPrimitive(type)
            : type.Shape == CliTypeShape.Named &&
              _snapshot.TypeLayouts.ObjectIdentities.TryGetValue(type, out var named)
                ? named
            : type.Shape == CliTypeShape.Named
                ? GetObjectLayout(_types.ResolveTypeIdentity(type).Key)
                : throw Missing(type);

    private ObjectLayout GetPrimitive(CliTypeIdentity type)
    {
        try
        {
            return GetObjectLayout(_types.ResolveTypeIdentity(type).Key);
        }
        catch (CompilerException exception)
            when (exception.Diagnostic.Code == DiagnosticCode.UnsupportedMetadata)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedMetadata,
                $"runtime type identity for '{type.CanonicalName}' is not supported"));
        }
    }

    private static CompilerException Missing(object type) => new(new CompilerDiagnostic(
        DiagnosticCode.RuntimeContract,
        $"object layout for '{type}' was not generated"));
}
