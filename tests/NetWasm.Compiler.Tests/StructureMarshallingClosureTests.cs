// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// The types Marshal's structure members reach through fields: an embedded formatted class (also through a base class
// and a nested structure), a ByValArray's structure, and a ByValArray's enum, whose array is created; not an array
// without ByValArray, a class without layout, an interface, a generic type or a static field.
public sealed class StructureMarshallingClosureTests
{
    private static readonly AssemblyIdentity Assembly = new("Closure");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);

    [Fact]
    public void FieldsReachEmbeddedClassesAndArrayStructuresOnly()
    {
        var types = new Types();
        var inner = types.Class("Inner", [("X", Int32, null, false)]);
        var deep = types.Class("Deep", [("Y", Int32, null, false)]);
        var element = types.Struct("Element", [("Deep", deep, null, false)]);
        var nested = types.Struct("Nested", [("Inner", inner, null, false)]);
        var automatic = types.Class("Automatic", [], CliTypeLayoutKind.Auto);
        var face = types.Class("Face", []);
        types.Change(face, definition => definition with { IsInterface = true });
        var generic = types.Class("Generic`1", []);
        types.Change(generic, definition => definition with { GenericArity = 1 });
        var color = types.Struct("Color", []);
        types.Change(color, definition => definition with { IsEnum = true });
        var baseClass = types.Class("Base", [("Inherited", inner, null, false)]);
        var onlyStatic = types.Class("OnlyStatic", []);
        var holder = types.Class("Holder",
        [
            ("Nested", nested, null, false),
            ("Elements", CliTypeIdentity.SzArray(element), UnmanagedType.ByValArray, false),
            ("Plain", CliTypeIdentity.SzArray(element), null, false),
            ("Colors", CliTypeIdentity.SzArray(color), UnmanagedType.ByValArray, false),
            ("Automatic", automatic, null, false),
            ("Face", face, null, false),
            ("Generic", generic, null, false),
            ("Static", onlyStatic, null, true),
            ("Modified", CliTypeIdentity.Modified(inner, Int32, isRequired: false), null, false),
        ]);
        types.Bases[holder] = baseClass;

        var reach = new StructureMarshallingClosure(types, types, types).Reach([holder, holder, Int32]);

        Assert.Equal([deep, element, inner], reach.Types);
        Assert.Equal([deep, inner], reach.EmbeddedClasses);
        Assert.Equal([element], reach.ArrayElements);
        Assert.Equal([color], reach.EnumArrayElements);
    }

    // Several array structures and enums are each named once, in order; a ByValArray of a primitive or of a class
    // reaches nothing.
    [Fact]
    public void ArrayElementsAreOrderedAndOnlyStructuresAndEnumsCount()
    {
        var types = new Types();
        var second = types.Struct("Second", [("Y", Int32, null, false)]);
        var first = types.Struct("First", [("X", Int32, null, false)]);
        var shade = types.Struct("Shade", []);
        types.Change(shade, definition => definition with { IsEnum = true });
        var color = types.Struct("Color", []);
        types.Change(color, definition => definition with { IsEnum = true });
        var inner = types.Class("Inner", [("X", Int32, null, false)]);
        var holder = types.Class("Holder",
        [
            ("Seconds", CliTypeIdentity.SzArray(second), UnmanagedType.ByValArray, false),
            ("Firsts", CliTypeIdentity.SzArray(first), UnmanagedType.ByValArray, false),
            ("Shades", CliTypeIdentity.SzArray(shade), UnmanagedType.ByValArray, false),
            ("Colors", CliTypeIdentity.SzArray(color), UnmanagedType.ByValArray, false),
            ("Numbers", CliTypeIdentity.SzArray(Int32), UnmanagedType.ByValArray, false),
            ("Classes", CliTypeIdentity.SzArray(inner), UnmanagedType.ByValArray, false),
        ]);

        var reach = new StructureMarshallingClosure(types, types, types).Reach([holder]);

        Assert.Equal([first, second], reach.ArrayElements);
        Assert.Equal([color, shade], reach.EnumArrayElements);
        Assert.Empty(reach.EmbeddedClasses);
    }

    [Fact]
    public void RejectsMissingCollaboratorsAndTypes()
    {
        var types = new Types();

        Assert.Throws<ArgumentNullException>(() => new StructureMarshallingClosure(null!, types, types));
        Assert.Throws<ArgumentNullException>(() => new StructureMarshallingClosure(types, null!, types));
        Assert.Throws<ArgumentNullException>(() => new StructureMarshallingClosure(types, types, null!));
        Assert.Throws<ArgumentNullException>(() => new StructureMarshallingClosure(types, types, types).Reach(null!));
    }

    private sealed class Types : ITypeDefinitionResolver, IFieldRepository, IBaseTypeResolver
    {
        private readonly Dictionary<CliTypeIdentity, TypeDefinitionModel> _definitions = [];
        private readonly Dictionary<EntityKey, FieldDefinitionModel> _fields = [];
        private int _token = 0x02000001;
        private int _field = 0x04000001;

        public Dictionary<CliTypeIdentity, CliTypeIdentity> Bases { get; } = [];

        public CliTypeIdentity Class(string name,
            (string Name, CliTypeIdentity Type, UnmanagedType? MarshalAs, bool IsStatic)[] fields,
            CliTypeLayoutKind layout = CliTypeLayoutKind.Sequential) => Add(name, false, fields, layout);

        public CliTypeIdentity Struct(string name,
            (string Name, CliTypeIdentity Type, UnmanagedType? MarshalAs, bool IsStatic)[] fields) =>
            Add(name, true, fields, CliTypeLayoutKind.Sequential);

        public void Change(CliTypeIdentity type, Func<TypeDefinitionModel, TypeDefinitionModel> change) =>
            _definitions[type] = change(_definitions[type]);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => _definitions[identity];

        public FieldDefinitionModel GetField(EntityKey key) => _fields[key];

        public CliTypeIdentity? Resolve(CliTypeIdentity type) => Bases.GetValueOrDefault(type);

        private CliTypeIdentity Add(string name, bool valueType,
            (string Name, CliTypeIdentity Type, UnmanagedType? MarshalAs, bool IsStatic)[] fields, CliTypeLayoutKind layout)
        {
            var type = CliTypeIdentity.Named(Assembly, "Closure", name, valueType);
            var key = new EntityKey(Assembly, _token++);
            var keys = new List<EntityKey>();
            foreach (var field in fields)
            {
                var fieldKey = new EntityKey(Assembly, _field++);
                _fields[fieldKey] = new FieldDefinitionModel(fieldKey, key, field.Name, field.Type, field.IsStatic)
                {
                    MarshalAs = field.MarshalAs,
                };
                keys.Add(fieldKey);
            }
            _definitions[type] = new TypeDefinitionModel(key, "Closure", name, valueType, [.. keys], [])
            {
                LayoutKind = layout,
            };
            return type;
        }
    }
}
