// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

// In-memory value types and formatted classes for native layouts: each added type's definition and instance fields.
internal sealed class NativeStructSource : INativeStructSource, IMetadataIdentityBaseTypeResolver
{
    public static AssemblyIdentity Assembly { get; } = new("Native");

    private readonly Dictionary<string, (TypeDefinitionModel, ImmutableArray<FieldDefinitionModel>)> _types = [];

    // A formatted class's base, when a test sets one.
    public Dictionary<CliTypeIdentity, CliTypeIdentity> Bases { get; } = [];

    public CliTypeIdentity? GetBaseType(CliTypeIdentity type) => Bases.GetValueOrDefault(type);

    private int _token = 0x02000001;
    private int _field = 0x04000001;

    public CliTypeIdentity Add(string name, (string Name, CliTypeIdentity Type, UnmanagedType? MarshalAs)[] fields,
        CliTypeLayoutKind layout = CliTypeLayoutKind.Sequential, int pack = 0, bool unicode = false,
        int[]? offsets = null, bool complex = false, bool valueType = true, int? sizeConst = null, int declaredSize = 0,
        UnmanagedType? subtype = null)
    {
        var type = CliTypeIdentity.Named(Assembly, "Native", name, valueType);
        var key = new EntityKey(Assembly, _token++);
        var models = fields.Select((field, index) => new FieldDefinitionModel(new(Assembly, _field++), key, field.Name,
            field.Type, false)
        {
            MarshalAs = field.MarshalAs,
            ExplicitOffset = offsets?[index],
            HasComplexMarshalAs = complex || field.MarshalAs is UnmanagedType.ByValTStr or UnmanagedType.ByValArray,
            MarshalAsSizeConst = field.MarshalAs is UnmanagedType.ByValTStr or UnmanagedType.ByValArray ? sizeConst : null,
            MarshalAsArraySubType = field.MarshalAs is UnmanagedType.ByValArray ? subtype : null,
        }).ToImmutableArray();
        var definition = new TypeDefinitionModel(key, "Native", name, valueType, [.. models.Select(field => field.Key)], [])
        {
            LayoutKind = layout,
            PackingSize = pack,
            DeclaredSize = declaredSize,
            Attributes = unicode ? 0x10000 : 0,
        };
        _types[type.CanonicalName] = (definition, models);
        return type;
    }

    public (TypeDefinitionModel Definition, ImmutableArray<FieldDefinitionModel> Fields)? Describe(CliTypeIdentity type) =>
        _types.TryGetValue(type.CanonicalName, out var described) ? described : null;

    public void Change(CliTypeIdentity type, Func<TypeDefinitionModel, TypeDefinitionModel> change)
    {
        var (definition, fields) = _types[type.CanonicalName];
        _types[type.CanonicalName] = (change(definition), fields);
    }
}
