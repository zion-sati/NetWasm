// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class StructMarshallingDataBuilderTests
{
    private static readonly AssemblyIdentity Assembly = new("struct-marshalling");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly string[] BlittablePrimitives =
        ["i1", "u1", "i2", "u2", "i4", "u4", "i8", "u8", "f4", "f8", "nativeint", "nativeuint"];
    private static readonly int[] Packings = [1, 2, 4, 8, 16, 32, 64, 128];

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void PublishesVersionedDescriptorsWithOptionalFieldNames(WasmTarget target, bool fieldNames)
    {
        var fixture = new Fixture(WasmTargetLayout.For(target));
        var point = fixture.Struct("Point", 8, 4, ("X", Int32, 0), ("Y", Int32, 4));
        var state = new ManagedStaticDataBuildState { Cursor = 21 };
        state.Strings.Add("X", new StringLayout(400, 1, 8));
        state.Strings.Add("Y", new StringLayout(440, 1, 8));
        fixture.Collector([point], fieldNames, state).Collect();
        fixture.Builder([point], fieldNames, state).Build();

        var descriptor = Assert.Single(state.StructMarshalling);
        Assert.Equal(point, descriptor.Type);
        Assert.Equal(40, descriptor.TypeId);
        Assert.Equal(24, descriptor.Address);
        var segment = Assert.Single(state.Segments);
        Assert.Equal(24, segment.Address);
        var bytes = segment.Data.AsSpan();
        var addressSize = WasmTargetLayout.For(target).AddressSize;
        Assert.Equal(1, Int(bytes, 0));
        Assert.Equal(StructMarshallingDataCollector.ValueType, Int(bytes, 4));
        Assert.Equal(8, Int(bytes, 8));
        Assert.Equal(WasmTargetLayout.Align(WasmTargetLayout.For(target).ObjectHeaderSize, 4), Int(bytes, 12));
        Assert.Equal(2, Int(bytes, 16));
        var offsets = Address(bytes, 24, addressSize);
        Assert.Equal(24 + 24 + (2 * addressSize), offsets);
        Assert.Equal(0, Int(bytes, offsets - 24));
        Assert.Equal(4, Int(bytes, offsets - 24 + 4));
        var names = Address(bytes, 24 + addressSize, addressSize);
        if (!fieldNames)
        {
            Assert.Equal(0, names);
            Assert.Equal(offsets + 8, state.Cursor);
            return;
        }
        Assert.Equal(400, Address(bytes, names - 24, addressSize));
        Assert.Equal(440, Address(bytes, names - 24 + addressSize, addressSize));
        Assert.Equal(names + (2 * addressSize), state.Cursor);
    }

    [Fact]
    public void ClassifiesConversionInvalidEnumAndConstructorLayouts()
    {
        var fixture = new Fixture(WasmTargetLayout.Wasm32);
        var withBool = fixture.Struct("WithBool", 8, 4,
            ("Flag", CliTypeIdentity.Primitive("bool", CliValueKind.I4), 0), ("X", Int32, 4));
        var withString = fixture.Struct("WithString", 4, 4,
            ("S", CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false), 0));
        var auto = fixture.Struct("Auto", 4, 4, CliTypeLayoutKind.Auto, ("X", Int32, 0));
        var formatted = fixture.Class("Formatted", CliTypeLayoutKind.Sequential);
        var plain = fixture.Class("Plain", CliTypeLayoutKind.Auto);
        var color = fixture.Enum("Color");
        var constructed = fixture.Struct("WithConstructor", 4, 4, ("X", Int32, 0));
        fixture.AddDefaultConstructor(constructed);
        var nested = fixture.Struct("Nested", 12, 4, ("Inner", withBool, 0), ("Y", Int32, 8));
        var state = new ManagedStaticDataBuildState();
        fixture.Collector([withBool, withString, auto, formatted, plain, color, constructed, nested], true, state).Collect();

        var flags = state.PendingStructMarshalling.ToDictionary(pending => pending.Type, pending => pending.Flags);
        var pendings = state.PendingStructMarshalling.ToDictionary(pending => pending.Type);
        const int value = StructMarshallingDataCollector.ValueType;
        const int native = value | StructMarshallingDataCollector.NativeLayout;
        Assert.Equal(native, flags[withBool]);
        // A string field converts to a pointer to a native copy, and a formatted class converts to its native layout.
        Assert.Equal(native, flags[withString]);
        Assert.Equal(value | StructMarshallingDataCollector.InvalidLayout, flags[auto]);
        Assert.Equal(StructMarshallingDataCollector.NativeLayout, flags[formatted]);
        Assert.Equal(StructMarshallingDataCollector.InvalidLayout, flags[plain]);
        Assert.Equal(value | StructMarshallingDataCollector.InvalidLayout, flags[color]);
        Assert.Equal(value | StructMarshallingDataCollector.DefaultConstructor, flags[constructed]);
        Assert.Equal(native, flags[nested]);
        Assert.All(state.PendingStructMarshalling.Where(pending => pending.Type != constructed &&
            (pending.Flags & StructMarshallingDataCollector.NativeLayout) == 0), pending => Assert.Empty(pending.Fields));

        // A bool is a 4-byte BOOL, and a nested struct that converts is copied through its own steps.
        Assert.Equal(8, pendings[withBool].Size);
        Assert.Equal([("Flag", 0), ("X", 4)], pendings[withBool].Fields);
        Assert.Equal([new NativeConversionStep(NativeConversionKind.Boolean, 0, 0, 4),
            new NativeConversionStep(NativeConversionKind.Copy, 4, 4, 4)], pendings[withBool].Steps);
        Assert.Equal(12, pendings[nested].Size);
        Assert.Equal([("Inner", 0), ("Y", 8)], pendings[nested].Fields);
        Assert.Equal([new NativeConversionStep(NativeConversionKind.Boolean, 0, 0, 4),
            new NativeConversionStep(NativeConversionKind.Copy, 4, 4, 4),
            new NativeConversionStep(NativeConversionKind.Copy, 8, 8, 4)], pendings[nested].Steps);
    }

    // A struct that converts publishes its native size and field offsets, then its conversion steps after the offsets.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PublishesNativeLayoutConversionSteps(WasmTarget target)
    {
        var fixture = new Fixture(WasmTargetLayout.For(target));
        var pair = fixture.Struct("Pair", 4, 2, ("X", CliTypeIdentity.Primitive("u1", CliValueKind.I4), 0),
            ("C", CliTypeIdentity.Primitive("char", CliValueKind.I4), 2));
        var state = new ManagedStaticDataBuildState { Cursor = 16 };
        fixture.Collector([pair], false, state).Collect();
        fixture.Builder([pair], false, state).Build();

        var bytes = Assert.Single(state.Segments).Data.AsSpan();
        var addressSize = WasmTargetLayout.For(target).AddressSize;
        Assert.Equal(StructMarshallingDataCollector.ValueType | StructMarshallingDataCollector.NativeLayout, Int(bytes, 4));
        Assert.Equal(2, Int(bytes, 8));
        Assert.Equal(2, Int(bytes, 16));
        Assert.Equal(2, Int(bytes, 20));
        var offsets = Address(bytes, 24, addressSize) - 16;
        Assert.Equal(0, Address(bytes, 24 + addressSize, addressSize));
        Assert.Equal([0, 1], new[] { Int(bytes, offsets), Int(bytes, offsets + 4) });
        // Each step is kind, managed offset, native offset, size, count and element.
        var steps = new int[2 * StructMarshallingDataBuilder.StepInts];
        for (var index = 0; index < steps.Length; index++)
            steps[index] = Int(bytes, offsets + 8 + (index * 4));
        Assert.Equal([(int)NativeConversionKind.Copy, 0, 0, 1, 0, 0, (int)NativeConversionKind.Ansi, 2, 1, 1, 0, 0], steps);
        Assert.Equal(16 + offsets + 8 + 48, state.Cursor);
    }

    // An embedded formatted class converts through its own descriptor, which its step names by the class's type ID
    // where other steps name an array's element.
    [Fact]
    public void AnEmbeddedClassStepNamesItsClassByTypeId()
    {
        var fixture = new Fixture(WasmTargetLayout.Wasm32);
        var inner = fixture.FormattedClass("Inner", ("X", Int32, 8));
        var outer = fixture.FormattedClass("Outer", ("A", Int32, 8), ("Inner", inner, 12));
        var state = new ManagedStaticDataBuildState { Cursor = 16 };
        fixture.Collector([outer], false, state).Collect();
        fixture.Builder([outer], false, state).Build();

        var pending = Assert.Single(state.PendingStructMarshalling);
        Assert.Equal(new NativeConversionStep(NativeConversionKind.EmbeddedClass, 12, 4, 4) { TypeId = 40 }, pending.Steps[1]);
        var bytes = Assert.Single(state.Segments).Data.AsSpan();
        var offsets = Address(bytes, 24, 4) - 16;
        // The fields' two offsets, then the copy step, then the embedded class's step, whose last value is the ID.
        Assert.Equal((int)NativeConversionKind.EmbeddedClass, Int(bytes, offsets + 8 + (6 * 4)));
        Assert.Equal(40, Int(bytes, offsets + 8 + (11 * 4)));
    }

    // A formatted class converts to its native layout from the start of its object: its managed offsets count from the
    // object and its payload offset is 0; a string field is a pointer to a native UTF-8 copy.
    [Fact]
    public void AFormattedClassConvertsFromItsObject()
    {
        var fixture = new Fixture(WasmTargetLayout.Wasm32);
        var text = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);
        var formatted = fixture.FormattedClass("Named", ("A", Int32, 8), ("Name", text, 12));
        var state = new ManagedStaticDataBuildState();
        fixture.Collector([formatted], true, state).Collect();

        var pending = Assert.Single(state.PendingStructMarshalling);
        Assert.Equal((StructMarshallingDataCollector.NativeLayout, 8, 0), (pending.Flags, pending.Size, pending.PayloadOffset));
        Assert.Equal([("A", 0), ("Name", 4)], pending.Fields);
        Assert.Equal([new NativeConversionStep(NativeConversionKind.Copy, 8, 0, 4),
            new NativeConversionStep(NativeConversionKind.Utf8String, 12, 4, 4)], pending.Steps);
    }

    // Each reference field converts by its kind: a UTF-16 string pointer, an inline string (UTF-16 in a Unicode
    // structure), a SafeHandle or CriticalHandle, and an inline array of an enum, which names the enum by its type ID.
    [Fact]
    public void ReferenceFieldsConvertByTheirKind()
    {
        var fixture = new Fixture(WasmTargetLayout.Wasm32);
        var text = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);
        var shade = fixture.Enum("Shade");
        var holder = fixture.FormattedClass("Holder", ("Wide", text, 8), ("Inline", text, 12),
            ("Safe", CliTypeIdentity.Named(Assembly, "System.Runtime.InteropServices", "SafeHandle", false), 16),
            ("Critical", CliTypeIdentity.Named(Assembly, "System.Runtime.InteropServices", "CriticalHandle", false), 20),
            ("Shades", CliTypeIdentity.SzArray(shade), 24));
        fixture.ChangeField(holder, "Wide", field => field with { MarshalAs = UnmanagedType.LPWStr });
        fixture.ChangeField(holder, "Inline", field => Inline(field, UnmanagedType.ByValTStr, 4));
        fixture.ChangeField(holder, "Shades", field => Inline(field, UnmanagedType.ByValArray, 2));
        var unicode = fixture.FormattedClass("Unicode", ("Inline", text, 8));
        fixture.Change(unicode, definition => definition with { Attributes = 0x10000 });
        fixture.ChangeField(unicode, "Inline", field => Inline(field, UnmanagedType.ByValTStr, 3));
        var state = new ManagedStaticDataBuildState();

        fixture.Collector([holder, unicode], false, state).Collect();

        var steps = state.PendingStructMarshalling.Select(pending => pending.Steps).ToArray();
        Assert.Equal([NativeConversionKind.Utf16String, NativeConversionKind.Utf8Inline, NativeConversionKind.SafeHandle,
            NativeConversionKind.CriticalHandle, NativeConversionKind.ArrayEnum], steps[0].Select(step => step.Kind));
        Assert.Equal((2, 4, 40), (steps[0][4].Count, steps[0][4].Size, steps[0][4].TypeId));
        Assert.Equal((NativeConversionKind.Utf16Inline, 3, 6), (steps[1][0].Kind, steps[1][0].Count, steps[1][0].Size));
    }

    // An inline array names its element: a blittable primitive is copied, a bool converts to a BOOL and a char to an
    // ANSI byte, or is copied as a UTF-16 code unit in a Unicode structure.
    [Theory]
    [InlineData("i1", false, nameof(NativeConversionKind.ArrayCopy), nameof(NativeArrayElement.SByte))]
    [InlineData("u1", false, nameof(NativeConversionKind.ArrayCopy), nameof(NativeArrayElement.Byte))]
    [InlineData("i2", false, nameof(NativeConversionKind.ArrayCopy), nameof(NativeArrayElement.Int16))]
    [InlineData("u2", false, nameof(NativeConversionKind.ArrayCopy), nameof(NativeArrayElement.UInt16))]
    [InlineData("i4", false, nameof(NativeConversionKind.ArrayCopy), nameof(NativeArrayElement.Int32))]
    [InlineData("u4", false, nameof(NativeConversionKind.ArrayCopy), nameof(NativeArrayElement.UInt32))]
    [InlineData("i8", false, nameof(NativeConversionKind.ArrayCopy), nameof(NativeArrayElement.Int64))]
    [InlineData("u8", false, nameof(NativeConversionKind.ArrayCopy), nameof(NativeArrayElement.UInt64))]
    [InlineData("f4", false, nameof(NativeConversionKind.ArrayCopy), nameof(NativeArrayElement.Single))]
    [InlineData("f8", false, nameof(NativeConversionKind.ArrayCopy), nameof(NativeArrayElement.Double))]
    [InlineData("nativeint", false, nameof(NativeConversionKind.ArrayCopy), nameof(NativeArrayElement.IntPtr))]
    [InlineData("nativeuint", false, nameof(NativeConversionKind.ArrayCopy), nameof(NativeArrayElement.UIntPtr))]
    [InlineData("bool", false, nameof(NativeConversionKind.ArrayBoolean), nameof(NativeArrayElement.Boolean))]
    [InlineData("char", false, nameof(NativeConversionKind.ArrayAnsi), nameof(NativeArrayElement.Char))]
    [InlineData("char", true, nameof(NativeConversionKind.ArrayCopy), nameof(NativeArrayElement.Char))]
    public void AnInlineArrayNamesItsElement(string primitive, bool unicode, string kind, string element)
    {
        var fixture = new Fixture(WasmTargetLayout.Wasm32);
        var holder = fixture.FormattedClass("Holder",
            ("Items", CliTypeIdentity.SzArray(CliTypeIdentity.Primitive(primitive, CliValueKind.I4)), 8));
        if (unicode)
            fixture.Change(holder, definition => definition with { Attributes = 0x10000 });
        fixture.ChangeField(holder, "Items", field => Inline(field, UnmanagedType.ByValArray, 2));
        var state = new ManagedStaticDataBuildState();

        fixture.Collector([holder], false, state).Collect();

        var step = Assert.Single(Assert.Single(state.PendingStructMarshalling).Steps);
        Assert.Equal((kind, element, 2), (step.Kind.ToString(), step.Element.ToString(), step.Count));
    }

    // OffsetOf finds a class's own fields, private ones included, and the non-private ones it inherits; a formatted
    // class on a base without layout is a type desktop .NET cannot load.
    [Fact]
    public void InheritedPrivateFieldsAndInvalidBasesFollowDesktop()
    {
        var fixture = new Fixture(WasmTargetLayout.Wasm32);
        var parent = fixture.FormattedClass("Parent", ("Hidden", Int32, 8), ("Shown", Int32, 12));
        fixture.ChangeField(parent, "Hidden", field => field with { IsPrivate = true });
        var child = fixture.FormattedClass("Child", ("Own", Int32, 16));
        fixture.ChangeField(child, "Own", field => field with { IsPrivate = true });
        fixture.Bases[child] = parent;
        var onAutomatic = fixture.FormattedClass("OnAutomatic", ("X", Int32, 8));
        fixture.Bases[onAutomatic] = fixture.Class("Automatic", CliTypeLayoutKind.Auto);
        var state = new ManagedStaticDataBuildState();

        fixture.Collector([child, onAutomatic], false, state).Collect();

        var pending = state.PendingStructMarshalling;
        Assert.Equal([("Shown", 4), ("Own", 8)], pending[0].Fields);
        Assert.Equal((StructMarshallingDataCollector.InvalidType, 0), (pending[1].Flags, pending[1].Size));
    }

    private static FieldDefinitionModel Inline(FieldDefinitionModel field, UnmanagedType marshalAs, int count) =>
        field with { MarshalAs = marshalAs, HasComplexMarshalAs = true, MarshalAsSizeConst = count };

    [Fact]
    public void ProgramsWithoutMarshalledTypesPublishNothing()
    {
        var fixture = new Fixture(WasmTargetLayout.Wasm32);
        var state = new ManagedStaticDataBuildState();
        fixture.Collector([], true, state).Collect();
        fixture.Builder([], true, state).Build();

        Assert.Empty(state.StructMarshalling);
        Assert.Empty(state.Segments);
    }

    // A primitive marshals on its own as blittable storage of its own size; a native integer has the address size.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PrimitivesMarshalAtTheirOwnSize(WasmTarget target)
    {
        var fixture = new Fixture(WasmTargetLayout.For(target));
        (string Name, CliValueKind Kind, int Size)[] primitives =
        [
            ("i1", CliValueKind.I4, 1), ("u1", CliValueKind.I4, 1), ("i2", CliValueKind.I4, 2), ("u2", CliValueKind.I4, 2),
            ("i4", CliValueKind.I4, 4), ("u4", CliValueKind.I4, 4), ("f4", CliValueKind.F4, 4), ("i8", CliValueKind.I8, 8),
            ("u8", CliValueKind.I8, 8), ("f8", CliValueKind.F8, 8),
            ("nativeint", CliValueKind.NativeInt, WasmTargetLayout.For(target).AddressSize),
            ("nativeuint", CliValueKind.NativeInt, WasmTargetLayout.For(target).AddressSize),
        ];
        var types = primitives.Select(primitive => fixture.Primitive(primitive.Name, primitive.Kind)).ToArray();
        var state = new ManagedStaticDataBuildState();
        fixture.Collector([.. types], false, state).Collect();

        Assert.Equal(primitives.Select(primitive => primitive.Size), state.PendingStructMarshalling.Select(pending => pending.Size));
        Assert.All(state.PendingStructMarshalling, pending => Assert.Equal(StructMarshallingDataCollector.ValueType, pending.Flags));
    }

    // A field decides its struct's shape: void and typedref cannot be marshalled, a char and an embedded formatted class
    // give the struct a native layout, and a struct that is generic, byref-like, oddly packed, unlaid out or holding a
    // byref cannot be marshalled.
    [Fact]
    public void FieldsAndLayoutPropertiesDecideTheShape()
    {
        var fixture = new Fixture(WasmTargetLayout.Wasm32);
        var blittable = fixture.Struct("AllBlittable", 64, 8, [.. BlittablePrimitives.Select((name, index) =>
            ($"F{index}", CliTypeIdentity.Primitive(name, CliValueKind.I4), index * 4))]);
        var withVoid = fixture.Struct("WithVoid", 4, 4, ("V", CliTypeIdentity.Primitive("void", CliValueKind.Void), 0));
        var withTypedReference = fixture.Struct("WithTypedReference", 4, 4,
            ("T", CliTypeIdentity.Primitive("typedref", CliValueKind.ValueType), 0));
        var withChar = fixture.Struct("WithChar", 4, 4, ("C", CliTypeIdentity.Primitive("char", CliValueKind.I4), 0));
        var withClass = fixture.Struct("WithClass", 4, 4, ("R", fixture.Class("Referenced", CliTypeLayoutKind.Sequential), 0));
        var generic = fixture.Struct("Generic", 4, 4, ("X", Int32, 0));
        fixture.Change(generic, definition => definition with { GenericArity = 1 });
        var byRefLike = fixture.Struct("ByRefLike", 4, 4, ("X", Int32, 0));
        fixture.Change(byRefLike, definition => definition with { IsByRefLike = true });
        var oddlyPacked = fixture.Struct("OddlyPacked", 4, 4, ("X", Int32, 0));
        fixture.Change(oddlyPacked, definition => definition with { PackingSize = 3 });
        var unlaidOut = fixture.Struct("UnlaidOut", 4, 4, ("X", Int32, 0));
        fixture.RemoveLayout(unlaidOut);
        var withByReference = fixture.Struct("WithByReference", 4, 4, ("X", Int32, 0));
        fixture.AddByReference(withByReference);
        var packed = Packings.Select(packing =>
        {
            var type = fixture.Struct($"Packed{packing}", 4, 4, ("X", Int32, 0));
            fixture.Change(type, definition => definition with { PackingSize = packing });
            return type;
        }).ToArray();
        var state = new ManagedStaticDataBuildState();
        fixture.Collector([blittable, withVoid, withTypedReference, withChar, withClass, generic, byRefLike, oddlyPacked,
            unlaidOut, withByReference, .. packed], false, state).Collect();

        var flags = state.PendingStructMarshalling.ToDictionary(pending => pending.Type, pending => pending.Flags);
        const int value = StructMarshallingDataCollector.ValueType;
        const int invalid = value | StructMarshallingDataCollector.InvalidLayout;
        const int native = value | StructMarshallingDataCollector.NativeLayout;
        Assert.Equal(value, flags[blittable]);
        Assert.Equal([invalid, invalid, native, native, invalid, invalid, invalid, invalid, invalid],
            new[] { withVoid, withTypedReference, withChar, withClass, generic, byRefLike, oddlyPacked, unlaidOut,
                withByReference }.Select(type => flags[type]));
        Assert.All(packed, type => Assert.Equal(value, flags[type]));
    }

    // A pointer is blittable but has no value layout of its own to publish.
    [Fact]
    public void AMarshalledPointerWithoutALayoutIsAContractFault()
    {
        var fixture = new Fixture(WasmTargetLayout.Wasm32);
        var pointer = fixture.Register(CliTypeIdentity.UnmanagedPointer(Int32));

        var error = Assert.Throws<CompilerException>(() =>
            fixture.Collector([pointer], false, new ManagedStaticDataBuildState()).Collect());
        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
    }

    [Fact]
    public void AFieldNameWithoutStaticStorageIsAContractFault()
    {
        var fixture = new Fixture(WasmTargetLayout.Wasm32);
        var point = fixture.Struct("Unnamed", 4, 4, ("X", Int32, 0));
        var state = new ManagedStaticDataBuildState();
        fixture.Collector([point], true, state).Collect();

        var error = Assert.Throws<CompilerException>(() => fixture.Builder([point], true, state).Build());
        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
    }

    private static int Int(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);

    private static int Address(ReadOnlySpan<byte> bytes, int offset, int size) =>
        size == sizeof(int) ? Int(bytes, offset) : checked((int)BinaryPrimitives.ReadInt64LittleEndian(bytes[offset..]));

    private sealed class Fixture(WasmTargetLayout target) : ITypeDefinitionResolver, IFieldRepository,
        IMetadataIdentityBaseTypeResolver, INativeStructSource
    {
        private readonly Dictionary<CliTypeIdentity, TypeDefinitionModel> _types = [];
        private readonly Dictionary<EntityKey, FieldDefinitionModel> _fields = [];
        private readonly List<MethodDefinitionModel> _methods = [];
        private readonly ManagedValueLayoutState _values = new();
        private readonly Dictionary<EntityKey, ObjectLayout> _objects = [];
        private int _next;

        internal CliTypeIdentity Struct(string name, int size, int alignment,
            params (string Name, CliTypeIdentity Type, int Offset)[] fields) =>
            Struct(name, size, alignment, CliTypeLayoutKind.Sequential, fields);

        internal CliTypeIdentity Struct(string name, int size, int alignment, CliTypeLayoutKind layout,
            params (string Name, CliTypeIdentity Type, int Offset)[] fields)
        {
            var identity = Add(name, isValue: true, layout, fields);
            _values.Values.Add(identity, new ValueLayout(identity, size, alignment,
                fields.Any(field => !field.Type.IsValueType) ? [0] : []));
            return identity;
        }

        internal CliTypeIdentity Class(string name, CliTypeLayoutKind layout) => Add(name, isValue: false, layout, []);

        // A sequential class whose fields sit at the given offsets from the start of its object.
        internal CliTypeIdentity FormattedClass(string name, params (string Name, CliTypeIdentity Type, int Offset)[] fields) =>
            Add(name, isValue: false, CliTypeLayoutKind.Sequential, fields);

        // A primitive or pointer marshalled on its own: its definition has an object layout, and no value layout.
        internal CliTypeIdentity Primitive(string name, CliValueKind kind) =>
            Register(CliTypeIdentity.Primitive(name, kind));

        internal CliTypeIdentity Register(CliTypeIdentity identity)
        {
            var key = new EntityKey(Assembly, 0x02000001 + _next++);
            _types.Add(identity, new TypeDefinitionModel(key, "System", identity.CanonicalName, true, [], []));
            _objects.Add(key, new ObjectLayout(40 + _objects.Count, 16, []));
            return identity;
        }

        internal void Change(CliTypeIdentity type, Func<TypeDefinitionModel, TypeDefinitionModel> change) =>
            _types[type] = change(_types[type]);

        // Changes a field of the type, by its name.
        internal void ChangeField(CliTypeIdentity type, string name, Func<FieldDefinitionModel, FieldDefinitionModel> change)
        {
            var key = _types[type].Fields.Single(field => _fields[field].Name == name);
            _fields[key] = change(_fields[key]);
        }

        internal void RemoveLayout(CliTypeIdentity type) => _values.Values.Remove(type);

        internal void AddByReference(CliTypeIdentity type) =>
            _values.Values[type] = _values.Values[type] with { ByReferenceOffsets = [0] };

        internal CliTypeIdentity Enum(string name)
        {
            var identity = Add(name, isValue: true, CliTypeLayoutKind.Auto, [("value__", CliTypeIdentity.Primitive("i2", CliValueKind.I4), 0)]);
            _types[identity] = _types[identity] with { IsEnum = true, EnumUnderlyingType = CliTypeIdentity.Primitive("i2", CliValueKind.I4) };
            _values.Values.Add(identity, new ValueLayout(identity, 2, 2, []));
            return identity;
        }

        internal void AddDefaultConstructor(CliTypeIdentity type) =>
            _methods.Add(new MethodDefinitionModel(new(Assembly, 0x06000001 + _methods.Count), _types[type].Key,
                ".ctor", false, MethodSignatureModel.Create(CliValueKind.Void), 0));

        internal StructMarshallingDataCollector Collector(
            ImmutableArray<CliTypeIdentity> types, bool fieldNames, ManagedStaticDataBuildState state) =>
            new(this, this, [.. _methods], this, this, Program(types, fieldNames), Layouts(), state);

        // A formatted class's base, when a test sets one; none otherwise.
        internal Dictionary<CliTypeIdentity, CliTypeIdentity> Bases { get; } = [];

        public CliTypeIdentity? GetBaseType(CliTypeIdentity type) => Bases.GetValueOrDefault(type);

        internal StructMarshallingDataBuilder Builder(
            ImmutableArray<CliTypeIdentity> types, bool fieldNames, ManagedStaticDataBuildState state) =>
            new(Program(types, fieldNames), Layouts(), state);

        private static ReachableProgram Program(ImmutableArray<CliTypeIdentity> types, bool fieldNames) =>
            ValidationTestData.Program() with { StructMarshallingTypes = types, StructMarshallingFieldNames = fieldNames };

        private ManagedTypeLayouts Layouts() =>
            new(_objects.ToImmutableDictionary(),
                ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
                ImmutableDictionary<string, ObjectLayout>.Empty,
                ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
                _values, target, -1, -1, -1, -1);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => _types[identity];

        public FieldDefinitionModel GetField(EntityKey key) => _fields[key];

        public (TypeDefinitionModel Definition, ImmutableArray<FieldDefinitionModel> Fields)? Describe(CliTypeIdentity type)
        {
            var definition = ResolveTypeIdentity(type);
            return (definition, [.. definition.Fields.Select(GetField).Where(field => !field.IsStatic)]);
        }

        private CliTypeIdentity Add(string name, bool isValue, CliTypeLayoutKind layout,
            (string Name, CliTypeIdentity Type, int Offset)[] fields)
        {
            var key = new EntityKey(Assembly, 0x02000001 + _next++);
            ImmutableArray<EntityKey> fieldKeys = [.. fields.Select((_, index) => new EntityKey(Assembly, 0x04000001 + (_next * 16) + index))];
            for (var index = 0; index < fields.Length; index++)
            {
                var field = new FieldDefinitionModel(fieldKeys[index], key, fields[index].Name,
                    fields[index].Type.StackKind, IsStatic: false)
                {
                    SignatureType = fields[index].Type,
                };
                _fields.Add(fieldKeys[index], field);
                _values.Fields.Add(fieldKeys[index], new FieldLayout(fields[index].Offset));
            }
            var definition = new TypeDefinitionModel(key, "Fixtures", name, isValue, fieldKeys, []) { LayoutKind = layout };
            var identity = CliTypeIdentity.FromDefinition(definition);
            _types.Add(identity, definition);
            _objects.Add(key, new ObjectLayout(40 + _objects.Count, 16, []));
            return identity;
        }
    }
}
