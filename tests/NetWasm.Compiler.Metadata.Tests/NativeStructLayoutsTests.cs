// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata.Tests;

// Structs as desktop .NET's runtime marshalling lays them out on Unix (Marshal.SizeOf on desktop .NET agrees with each
// size here), and the native signature a P/Invoke's stub calls with them.
public sealed class NativeStructLayoutsTests
{
    private static readonly AssemblyIdentity Assembly = new("Native");
    private static readonly CliTypeIdentity Bool = CliTypeIdentity.Primitive("bool", CliValueKind.I4);
    private static readonly CliTypeIdentity Char = CliTypeIdentity.Primitive("char", CliValueKind.I4);
    private static readonly CliTypeIdentity Byte = CliTypeIdentity.Primitive("u1", CliValueKind.I4);
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Double = CliTypeIdentity.Primitive("f8", CliValueKind.F8);
    private static readonly CliTypeIdentity NativeInt = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
    private static readonly CliTypeIdentity Text = CliTypeIdentity.Named(Assembly, "System", "String", false);

    [Fact]
    public void ABoolIsAFourByteBoolBetweenAlignedInts()
    {
        var source = new NativeStructSource();
        var flagged = source.Add("Flagged", fields: [("A", Int32, null), ("B", Bool, null), ("C", Int32, null)]);

        var layout = NativeStructLayouts.Plan(flagged, source, 4)!;

        Assert.Equal((12, 4, true), (layout.Size, layout.Alignment, layout.Converts));
        Assert.Equal([0, 4, 8], layout.Fields.Select(field => field.Offset));
        Assert.Equal("primitive:i4", layout.Fields[1].Conversion!.Native.CanonicalName);
        Assert.Null(layout.Scalar);
    }

    [Theory]
    [InlineData(UnmanagedType.U1, 8, "primitive:u1")]
    [InlineData(UnmanagedType.I1, 8, "primitive:i1")]
    [InlineData(UnmanagedType.Bool, 8, "primitive:i4")]
    public void ABoolMarshalledAsAByteTakesOneByte(UnmanagedType marshalAs, int size, string native)
    {
        var source = new NativeStructSource();
        var flag = source.Add("ByteFlag", fields: [("A", Int32, null), ("B", Bool, marshalAs)]);

        var layout = NativeStructLayouts.Plan(flag, source, 4)!;

        Assert.Equal(size, layout.Size);
        Assert.Equal(native, layout.Fields[1].Conversion!.Native.CanonicalName);
    }

    [Theory]
    [InlineData(false, null, 2, 1)]
    [InlineData(true, null, 4, 2)]
    [InlineData(false, UnmanagedType.U2, 4, 2)]
    [InlineData(true, UnmanagedType.U1, 2, 1)]
    public void ACharIsAnAnsiByteUnlessTheStructOrFieldSaysUnicode(bool unicode, UnmanagedType? marshalAs, int size,
        int offset)
    {
        var source = new NativeStructSource();
        var pair = source.Add("Pair", unicode: unicode, fields: [("X", Byte, null), ("C", Char, marshalAs)]);

        var layout = NativeStructLayouts.Plan(pair, source, 4)!;

        Assert.Equal((size, offset), (layout.Size, layout.Fields[1].Offset));
    }

    [Fact]
    public void ANestedStructThatConvertsLaysOutItsOwnFields()
    {
        var source = new NativeStructSource();
        var flagged = source.Add("Flagged", fields: [("A", Int32, null), ("B", Bool, null), ("C", Int32, null)]);
        var outer = source.Add("Outer", fields: [("Inner", flagged, null), ("Flag", Bool, null)]);

        var layout = NativeStructLayouts.Plan(outer, source, 4)!;

        Assert.Equal(16, layout.Size);
        Assert.Equal(12, layout.Fields[1].Offset);
        Assert.Equal(12, layout.Fields[0].Nested!.Size);
    }

    [Fact]
    public void AStructOfOneConvertingScalarPassesAsThatScalar()
    {
        var source = new NativeStructSource();
        var flag = source.Add("Flag", fields: [("B", Bool, null)]);
        var wrapper = source.Add("Wrapper", fields: [("Flag", flag, null)]);
        var measure = source.Add("Measure", fields: [("D", Double, null), ("B", Bool, null)]);

        Assert.Equal("primitive:i4", NativeStructLayouts.Plan(flag, source, 4)!.Scalar!.CanonicalName);
        Assert.Equal("primitive:i4", NativeStructLayouts.Plan(wrapper, source, 4)!.Scalar!.CanonicalName);
        Assert.Equal((16, 8), NativeStructLayouts.Plan(measure, source, 4) is { } layout ? (layout.Size, layout.Alignment) : default);
        Assert.Null(NativeStructLayouts.Plan(measure, source, 4)!.Scalar);
    }

    [Fact]
    public void APointerSizedFieldLaysOutByTheTargetsPointerSize()
    {
        var source = new NativeStructSource();
        var flag = source.Add("PointerFlag", fields: [("P", NativeInt, null), ("B", Bool, null)]);

        var narrow = NativeStructLayouts.Plan(flag, source, 4)!;
        var wide = NativeStructLayouts.Plan(flag, source, 8)!;

        Assert.Equal((8, 4), (narrow.Size, narrow.Fields[1].Offset));
        Assert.Equal((16, 8), (wide.Size, wide.Fields[1].Offset));
    }

    [Fact]
    public void PackAndExplicitOffsetsPlaceFieldsAsDeclared()
    {
        var source = new NativeStructSource();
        var packed = source.Add("Packed", pack: 1, fields: [("X", Byte, null), ("B", Bool, null)]);
        var explicitLayout = source.Add("Explicit", layout: CliTypeLayoutKind.Explicit,
            fields: [("B", Bool, null), ("X", Byte, null)], offsets: [4, 0]);

        var packedLayout = NativeStructLayouts.Plan(packed, source, 4)!;
        var explicitPlan = NativeStructLayouts.Plan(explicitLayout, source, 4)!;

        Assert.Equal((5, 1), (packedLayout.Size, packedLayout.Fields[1].Offset));
        Assert.Equal((8, 4), (explicitPlan.Size, explicitPlan.Fields[0].Offset));
    }

    [Fact]
    public void ABlittableStructConvertsNothing()
    {
        var source = new NativeStructSource();
        var point = source.Add("Point", fields: [("X", Int32, null), ("Y", Int32, null)]);

        Assert.False(NativeStructLayouts.Plan(point, source, 4)!.Converts);
    }

    [Fact]
    public void AStructRuntimeMarshallingCannotPassHasNoLayout()
    {
        var source = new NativeStructSource();
        var text = source.Add("WithText", fields: [("B", Bool, null), ("T", Text, null)]);
        var variant = source.Add("Variant", fields: [("B", Bool, UnmanagedType.VariantBool)]);
        var wideChar = source.Add("WideChar", fields: [("C", Char, UnmanagedType.I4)]);
        var auto = source.Add("Auto", layout: CliTypeLayoutKind.Auto, fields: [("B", Bool, null)]);
        var complex = source.Add("Complex", fields: [("B", Bool, null)], complex: true);
        var foreign = CliTypeIdentity.Named(new AssemblyIdentity("Other"), "Other", "Foreign", true);
        var holder = source.Add("Holder", fields: [("B", Bool, null), ("F", foreign, null)]);

        Assert.All(new[] { text, variant, wideChar, auto, complex, foreign, holder },
            type => Assert.Null(NativeStructLayouts.Plan(type, source, 4)));
    }

    [Fact]
    public void AStubTakesAConvertingStructAsTheCAbiPassesItsNativeCopy()
    {
        var source = new NativeStructSource();
        var flagged = source.Add("Flagged", fields: [("A", Int32, null), ("B", Bool, null), ("C", Int32, null)]);
        var flag = source.Add("Flag", fields: [("B", Bool, null)]);
        var point = source.Add("Point", fields: [("X", Int32, null), ("Y", Int32, null)]);
        NativeStructLayout? Structs(CliTypeIdentity type) => NativeStructLayouts.Plan(type, source, 4);
        var import = new NativeImportDeclaration("mule", "f", default, IsVarArg: false, HasMarshalling: false, null);
        var voidType = CliTypeIdentity.FromStackKind(CliValueKind.Void);

        var byValue = NativeMarshallingStubSignature.Lower(
            new MethodSignatureModel(Int32, [flagged, flag, CliTypeIdentity.ManagedByReference(flag), point]), import, Structs)!;
        var indirectResult = NativeMarshallingStubSignature.Lower(new MethodSignatureModel(flagged, [Int32]), import, Structs)!;
        var scalarResult = NativeMarshallingStubSignature.Lower(new MethodSignatureModel(flag, [Int32]), import, Structs)!;

        Assert.Equal(["primitive:nativeint", "primitive:i4", "primitive:nativeint", point.CanonicalName],
            byValue.ParameterSignatureTypes.Select(type => type.CanonicalName));
        Assert.Equal(voidType, indirectResult.ReturnSignatureType);
        Assert.Equal(["primitive:nativeint", "primitive:i4"], indirectResult.ParameterSignatureTypes.Select(type => type.CanonicalName));
        Assert.Equal(("primitive:i4", 1), (scalarResult.ReturnSignatureType.CanonicalName, scalarResult.ParameterSignatureTypes.Length));
        Assert.Null(NativeMarshallingStubSignature.Lower(new MethodSignatureModel(point, [point]), import, Structs));
    }

    private static readonly CliTypeIdentity String = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false);
    private static readonly CliTypeIdentity Object = CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference, false);

    [Fact]
    public void AFormattedClassLaysOutItsBaseClassFieldsFirst()
    {
        var source = new NativeStructSource();
        var parent = source.Add("Parent", valueType: false, fields: [("X", Int32, null), ("F", Bool, null)]);
        var child = source.Add("Child", valueType: false, fields: [("Y", Char, null), ("Z", Double, null)]);
        source.Bases[child] = parent;
        var automatic = source.Add("Automatic", valueType: false, layout: CliTypeLayoutKind.Auto, fields: [("Q", Int32, null)]);
        var onAutomatic = source.Add("OnAutomatic", valueType: false, fields: [("R", Int32, null)]);
        source.Bases[onAutomatic] = automatic;

        var layout = NativeStructLayouts.PlanStructure(child, source, 4, source).Layout!;

        // The base's int and 4-byte BOOL, then the ANSI char and the double, as desktop .NET lays them out.
        Assert.Equal([("X", 0), ("F", 4), ("Y", 8), ("Z", 16)], layout.Fields.Select(field => (field.Field.Name, field.Offset)));
        Assert.Equal((24, true), (layout.Size, layout.Converts));
        Assert.Null(NativeStructLayouts.Plan(child, source, 4));
        Assert.Equal((null, true), (NativeStructLayouts.PlanStructure(automatic, source, 4, source).Layout,
            NativeStructLayouts.PlanStructure(automatic, source, 4, source).Rejected));
        Assert.True(NativeStructLayouts.PlanStructure(onAutomatic, source, 4, source).Rejected);
    }

    // A formatted class embeds at its own layout's size and alignment, and a ByValArray of structures holds its
    // elements at the structure's size and alignment; an embedded interface or class without layout is rejected.
    [Fact]
    public void EmbeddedClassesAndArraysOfStructuresLayOutAsTheirOwnStructures()
    {
        var source = new NativeStructSource();
        var inner = source.Add("Inner", valueType: false, fields: [("X", Int32, null), ("F", Bool, null)]);
        var point = source.Add("Point", fields: [("X", Double, null), ("Y", Byte, null)]);
        var holder = source.Add("Holder", fields: [("Tag", Byte, null), ("Inner", inner, null),
            ("Points", CliTypeIdentity.SzArray(point), UnmanagedType.ByValArray)], sizeConst: 3);
        var automatic = source.Add("Automatic", valueType: false, layout: CliTypeLayoutKind.Auto, fields: [("Q", Int32, null)]);
        var face = source.Add("Face", valueType: false, fields: []);
        source.Change(face, definition => definition with { IsInterface = true });
        var holdsAutomatic = source.Add("HoldsAutomatic", fields: [("A", automatic, null)]);
        var holdsInterface = source.Add("HoldsInterface", fields: [("I", face, null)]);
        var holdsUnknown = source.Add("HoldsUnknown",
            fields: [("U", CliTypeIdentity.Named(Assembly, "Native", "Unknown", false), null)]);

        var layout = NativeStructLayouts.PlanStructure(holder, source, 4, source).Layout!;

        Assert.Equal([null, NativeReferenceKind.EmbeddedClass, NativeReferenceKind.ArrayStructure],
            layout.Fields.Select(field => field.Reference?.Kind));
        // The class's int and BOOL at 4, then three 16-byte points at their 8-byte alignment.
        Assert.Equal([(0, 1), (4, 8), (16, 48)], layout.Fields.Select(field => (field.Offset, field.Size)));
        Assert.Equal((inner, point, 3, 16), (layout.Fields[1].Reference!.Element, layout.Fields[2].Reference!.Element,
            layout.Fields[2].Reference!.Count, layout.Fields[2].Reference!.ElementSize));
        Assert.Equal(64, layout.Size);
        Assert.All(new[] { holdsAutomatic, holdsInterface }, type =>
            Assert.Equal((null, true), (NativeStructLayouts.PlanStructure(type, source, 4, source).Layout,
                NativeStructLayouts.PlanStructure(type, source, 4, source).Rejected)));
        Assert.Equal((null, false), (NativeStructLayouts.PlanStructure(holdsUnknown, source, 4, source).Layout,
            NativeStructLayouts.PlanStructure(holdsUnknown, source, 4, source).Rejected));
    }

    // A ByValArray of an enum holds its underlying values; one with a subtype is not converted.
    [Fact]
    public void AnEnumArrayHoldsItsUnderlyingValues()
    {
        var source = new NativeStructSource();
        var shade = source.Add("Shade", fields: []);
        source.Change(shade, definition => definition with { IsEnum = true, EnumUnderlyingType = Byte });
        var shades = source.Add("Shades", fields: [("Items", CliTypeIdentity.SzArray(shade), UnmanagedType.ByValArray)], sizeConst: 3);

        var items = NativeStructLayouts.PlanStructure(shades, source, 4, source).Layout!.Fields[0];

        Assert.Equal((NativeReferenceKind.ArrayEnum, 3, 1, shade), (items.Reference!.Kind, items.Size,
            items.Reference.ElementSize, items.Reference.Element));
    }

    // A formatted class on a base without layout is a type desktop .NET cannot load; one on a formatted base with a
    // field desktop rejects is only rejected, and one that plans has neither.
    [Fact]
    public void AFormattedClassOnABaseWithoutLayoutIsInvalid()
    {
        var source = new NativeStructSource();
        var automatic = source.Add("Automatic", valueType: false, layout: CliTypeLayoutKind.Auto, fields: [("Q", Int32, null)]);
        var onAutomatic = source.Add("OnAutomatic", valueType: false, fields: [("R", Int32, null)]);
        source.Bases[onAutomatic] = automatic;
        var rejecting = source.Add("Rejecting", valueType: false, fields: [("O", Object, null)]);
        var onRejecting = source.Add("OnRejecting", valueType: false, fields: [("S", Int32, null)]);
        source.Bases[onRejecting] = rejecting;
        var plain = source.Add("Plain", valueType: false, fields: [("T", Int32, null)]);

        var invalid = NativeStructLayouts.PlanStructure(onAutomatic, source, 4, source);
        var rejected = NativeStructLayouts.PlanStructure(onRejecting, source, 4, source);
        var planned = NativeStructLayouts.PlanStructure(plain, source, 4, source);

        Assert.Equal((null, true), (invalid.Layout, invalid.InvalidBase));
        Assert.Equal((true, false), (rejected.Rejected, rejected.InvalidBase));
        Assert.Equal((false, false), (planned.Rejected, planned.InvalidBase));
    }

    // A SafeHandle or CriticalHandle field, of the handle type or one derived from it, is a pointer-sized handle.
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void HandleFieldsArePointerSizedHandles(int pointerSize)
    {
        var source = new NativeStructSource();
        var safe = Handle("System.Runtime.InteropServices", "SafeHandle");
        var derivedSafe = Handle("Native", "OwnHandle");
        var critical = Handle("System.Runtime.InteropServices", "CriticalHandle");
        var derivedCritical = Handle("Native", "OwnCritical");
        source.Bases[derivedSafe] = safe;
        source.Bases[derivedCritical] = critical;
        var holder = source.Add("Holder", fields: [("A", Int32, null), ("Safe", safe, null), ("DerivedSafe", derivedSafe, null),
            ("Critical", critical, null), ("DerivedCritical", derivedCritical, null)]);

        var layout = NativeStructLayouts.PlanStructure(holder, source, pointerSize, source).Layout!;

        Assert.Equal([null, NativeReferenceKind.SafeHandle, NativeReferenceKind.SafeHandle, NativeReferenceKind.CriticalHandle,
            NativeReferenceKind.CriticalHandle], layout.Fields.Select(field => field.Reference?.Kind));
        Assert.Equal([4, pointerSize, pointerSize, pointerSize, pointerSize], layout.Fields.Select(field => field.Size));
        Assert.Null(NativeStructLayouts.Plan(holder, source, pointerSize));

        static CliTypeIdentity Handle(string ns, string name) => CliTypeIdentity.Named(Assembly, ns, name, false);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void StringFieldsArePointersToNativeCopiesOrInlineCharacters(int pointerSize)
    {
        var source = new NativeStructSource();
        var texts = source.Add("Texts", fields: [("A", Int32, null), ("Default", String, null),
            ("Wide", String, UnmanagedType.LPWStr), ("Utf8", String, UnmanagedType.LPUTF8Str), ("Inline", String, UnmanagedType.ByValTStr)],
            sizeConst: 8);
        var unicode = source.Add("UnicodeTexts", unicode: true, fields: [("Default", String, null), ("Inline", String, UnmanagedType.ByValTStr)],
            sizeConst: 3);

        var layout = NativeStructLayouts.PlanStructure(texts, source, pointerSize, source).Layout!;
        var wide = NativeStructLayouts.PlanStructure(unicode, source, pointerSize, source).Layout!;

        Assert.Equal([null, NativeReferenceKind.Utf8Pointer, NativeReferenceKind.Utf16Pointer, NativeReferenceKind.Utf8Pointer,
            NativeReferenceKind.Utf8Inline], layout.Fields.Select(field => field.Reference?.Kind));
        Assert.Equal([0, pointerSize, 2 * pointerSize, 3 * pointerSize, 4 * pointerSize],
            layout.Fields.Select(field => field.Offset));
        Assert.Equal(Align(4 * pointerSize + 8, pointerSize), layout.Size);
        Assert.Equal([NativeReferenceKind.Utf16Pointer, NativeReferenceKind.Utf16Inline], wide.Fields.Select(field => field.Reference!.Kind));
        Assert.Equal((pointerSize, 6), (wide.Fields[1].Offset, wide.Fields[1].Size));
        // A stub takes none of them.
        Assert.Null(NativeStructLayouts.Plan(texts, source, pointerSize));
    }

    [Fact]
    public void InlineArraysHoldTheirElementsAndFieldsDesktopRejectsAreRejected()
    {
        var source = new NativeStructSource();
        var numbers = source.Add("Numbers", fields: [("A", Int32, null), ("Items", CliTypeIdentity.SzArray(Int32), UnmanagedType.ByValArray)],
            sizeConst: 4);
        var flags = source.Add("Flags", fields: [("Values", CliTypeIdentity.SzArray(Bool), UnmanagedType.ByValArray)], sizeConst: 3);
        var plainArray = source.Add("PlainArray", fields: [("Items", CliTypeIdentity.SzArray(Int32), null)]);
        var holdsObject = source.Add("HoldsObject", fields: [("O", Object, null)]);

        var items = NativeStructLayouts.PlanStructure(numbers, source, 4, source).Layout!.Fields[1];
        var values = NativeStructLayouts.PlanStructure(flags, source, 4, source).Layout!.Fields[0];

        Assert.Equal((4, 16, NativeReferenceKind.ArrayCopy, 4), (items.Offset, items.Size, items.Reference!.Kind, items.Reference.Count));
        Assert.Equal((12, NativeReferenceKind.ArrayBoolean, 4), (values.Size, values.Reference!.Kind, values.Reference.ElementSize));
        Assert.All(new[] { plainArray, holdsObject }, type =>
            Assert.Equal((null, true), (NativeStructLayouts.PlanStructure(type, source, 4, source).Layout,
                NativeStructLayouts.PlanStructure(type, source, 4, source).Rejected)));
    }

    [Fact]
    public void AFixedBufferOfUtf16CharsCopiesWholeButStillPassesThroughItsNativeCopy()
    {
        var source = new NativeStructSource();
        var buffer = source.Add("Buffer", unicode: true, declaredSize: 6, fields: [("FixedElementField", Char, null)]);
        var holder = source.Add("Holder", fields: [("A", Int32, null), ("Chars", buffer, null)]);
        var ansi = source.Add("AnsiBuffer", declaredSize: 3, fields: [("FixedElementField", Char, null)]);
        var ansiHolder = source.Add("AnsiHolder", fields: [("A", Int32, null), ("Chars", ansi, null)]);

        var layout = NativeStructLayouts.Plan(holder, source, 4)!;
        var ansiLayout = NativeStructLayouts.Plan(ansiHolder, source, 4)!;

        Assert.Equal((true, true), (layout.Converts, layout.CopiesWhole));
        Assert.Equal((6, true), (layout.Fields[1].Size, layout.Fields[1].Nested!.CopiesWhole));
        Assert.Equal((true, false), (ansiLayout.Converts, ansiLayout.CopiesWhole));
        Assert.False(ansiLayout.Fields[1].Nested!.CopiesWhole);
    }

    // Each primitive field at its native size, which is also its C alignment; a native-sized integer at the target's
    // pointer size.
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void EveryPrimitiveFieldTakesItsNativeSize(int pointerSize)
    {
        var source = new NativeStructSource();
        string[] names = ["i1", "u1", "i2", "u2", "i4", "u4", "f4", "i8", "u8", "f8", "nativeint", "nativeuint"];
        var primitives = source.Add("Primitives", fields: [.. names.Select(name =>
            (name, CliTypeIdentity.Primitive(name, CliValueKind.I4), (UnmanagedType?)null))]);

        var layout = NativeStructLayouts.Plan(primitives, source, pointerSize)!;

        Assert.Equal([1, 1, 2, 2, 4, 4, 4, 8, 8, 8, pointerSize, pointerSize], layout.Fields.Select(field => field.Size));
        Assert.Equal([0, 1, 2, 4, 8, 12, 16, 24, 32, 40, 48, 48 + pointerSize], layout.Fields.Select(field => field.Offset));
        Assert.False(layout.Converts);
    }

    // A pointer or function pointer field is pointer-sized; a modified field (volatile) is its unmodified type; a
    // nested struct that converts nothing is copied as it is; a [MarshalAs] that restates a primitive changes nothing.
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void PointersModifiedFieldsAndBlittableNestedStructsKeepTheirManagedForm(int pointerSize)
    {
        var source = new NativeStructSource();
        var point = source.Add("Point", fields: [("X", Int32, null), ("Y", Int32, null)]);
        var volatileInt = CliTypeIdentity.Modified(Int32,
            CliTypeIdentity.Named(Assembly, "System.Runtime.CompilerServices", "IsVolatile", false), true);
        var functionPointer = CliTypeIdentity.FunctionPointer(new(0, 0, 0, new MethodSignatureModel(Int32, [])));
        var holder = source.Add("Holder", fields: [("Pointer", CliTypeIdentity.UnmanagedPointer(Int32), null),
            ("Function", functionPointer, null), ("Volatile", volatileInt, null), ("Point", point, null),
            ("Restated", Int32, UnmanagedType.I4)]);

        var layout = NativeStructLayouts.Plan(holder, source, pointerSize)!;

        Assert.Equal([pointerSize, pointerSize, 4, 8, 4], layout.Fields.Select(field => field.Size));
        Assert.Equal(("primitive:i4", "primitive:i4"), (layout.Fields[2].Type.CanonicalName, layout.Fields[4].Type.CanonicalName));
        Assert.Null(layout.Fields[3].Nested);
        Assert.False(layout.Converts);
    }

    // An explicit field without an offset, a [MarshalAs] that does not fit a primitive, and an enum whose underlying
    // type has no native size each leave the struct without a layout; a member-less enum is only checked here.
    [Fact]
    public void FieldsWithoutANativePlaceLeaveTheStructWithoutALayout()
    {
        var source = new NativeStructSource();
        var unplaced = source.Add("Unplaced", layout: CliTypeLayoutKind.Explicit, fields: [("X", Int32, null)]);
        var widened = source.Add("Widened", fields: [("X", Int32, UnmanagedType.I8)]);
        var sizeless = source.Add("Sizeless", fields: []);
        source.Change(sizeless, definition => definition with { IsEnum = true, EnumUnderlyingType = Object });
        var holdsSizeless = source.Add("HoldsSizeless", fields: [("E", sizeless, null)]);

        Assert.All(new[] { unplaced, widened, holdsSizeless }, type => Assert.Null(NativeStructLayouts.Plan(type, source, 4)));
    }

    // An enum field takes its underlying type's native size, a bool or char enum (which IL allows) included.
    [Theory]
    [InlineData("u1", 1)]
    [InlineData("bool", 1)]
    [InlineData("char", 2)]
    [InlineData("i8", 8)]
    public void AnEnumFieldTakesItsUnderlyingSize(string underlying, int size)
    {
        var source = new NativeStructSource();
        var shade = source.Add("Shade", fields: []);
        source.Change(shade, definition => definition with
        {
            IsEnum = true,
            EnumUnderlyingType = CliTypeIdentity.Primitive(underlying, CliValueKind.I4),
        });
        var holdsShade = source.Add("HoldsShade", fields: [("E", shade, null)]);

        var field = NativeStructLayouts.Plan(holdsShade, source, 4)!.Fields[0];

        Assert.Equal((0, size, null), (field.Offset, field.Size, field.Conversion));
    }

    // Strings as LPStr and LPTStr; a string type Marshal's structure members do not convert (BStr, an empty ByValTStr),
    // a multidimensional array, a class that embeds itself or one whose fields do not convert are not converted, while
    // a StringBuilder is a field desktop .NET rejects.
    [Fact]
    public void ReferenceFieldsMarshalsStructureMembersDoNotConvertAreNotConverted()
    {
        var source = new NativeStructSource();
        var texts = source.Add("Texts", fields: [("Ansi", String, UnmanagedType.LPStr), ("Platform", String, UnmanagedType.LPTStr)]);
        var bstr = source.Add("Bstr", fields: [("B", String, UnmanagedType.BStr)]);
        var empty = source.Add("Empty", fields: [("T", String, UnmanagedType.ByValTStr)], sizeConst: 0);
        var grid = source.Add("Grid", fields: [("G", CliTypeIdentity.Array(Int32, 2), null)]);
        var cycle = CliTypeIdentity.Named(Assembly, "Native", "Cycle", false);
        var cycleType = source.Add("Cycle", valueType: false, fields: [("Next", cycle, null)]);
        var widened = source.Add("WidenedClass", valueType: false, fields: [("X", Int32, UnmanagedType.I8)]);
        var holdsWidened = source.Add("HoldsWidened", fields: [("W", widened, null)]);
        var builder = source.Add("Builder", fields: [("B", CliTypeIdentity.Named(Assembly, "System.Text", "StringBuilder", false), null)]);

        var layout = NativeStructLayouts.PlanStructure(texts, source, 4, source).Layout!;

        Assert.Equal([NativeReferenceKind.Utf8Pointer, NativeReferenceKind.Utf16Pointer], layout.Fields.Select(field => field.Reference!.Kind));
        Assert.All(new[] { bstr, empty, grid, cycleType, holdsWidened }, type =>
            Assert.Equal((null, false), (NativeStructLayouts.PlanStructure(type, source, 4, source).Layout,
                NativeStructLayouts.PlanStructure(type, source, 4, source).Rejected)));
        Assert.Equal((null, true), (NativeStructLayouts.PlanStructure(builder, source, 4, source).Layout,
            NativeStructLayouts.PlanStructure(builder, source, 4, source).Rejected));
    }

    // A ByValArray of booleans is 4-byte BOOLs (Bool) or bytes (U1, I1); of chars, UTF-16 code units (U2, I2, or a
    // Unicode structure's default) or ANSI bytes (U1, I1, or an ANSI structure's default); any other subtype, or one on
    // a blittable primitive, a reference element or an unreadable struct element, is not converted.
    [Theory]
    [InlineData("bool", UnmanagedType.Bool, false, nameof(NativeReferenceKind.ArrayBoolean), 4)]
    [InlineData("bool", UnmanagedType.U1, false, nameof(NativeReferenceKind.ArrayBoolean), 1)]
    [InlineData("bool", UnmanagedType.I1, false, nameof(NativeReferenceKind.ArrayBoolean), 1)]
    [InlineData("bool", UnmanagedType.I4, false, null, 0)]
    [InlineData("char", UnmanagedType.U2, false, nameof(NativeReferenceKind.ArrayCopy), 2)]
    [InlineData("char", UnmanagedType.I2, false, nameof(NativeReferenceKind.ArrayCopy), 2)]
    [InlineData("char", null, true, nameof(NativeReferenceKind.ArrayCopy), 2)]
    [InlineData("char", null, false, nameof(NativeReferenceKind.ArrayAnsi), 1)]
    [InlineData("char", UnmanagedType.U1, true, nameof(NativeReferenceKind.ArrayAnsi), 1)]
    [InlineData("char", UnmanagedType.I1, false, nameof(NativeReferenceKind.ArrayAnsi), 1)]
    [InlineData("char", UnmanagedType.I4, false, null, 0)]
    [InlineData("i4", UnmanagedType.I4, false, null, 0)]
    [InlineData("string", null, false, null, 0)]
    public void ArrayElementsFollowTheirSubtype(string element, UnmanagedType? subtype, bool unicode,
        string? kind, int elementSize)
    {
        var source = new NativeStructSource();
        var elementType = CliTypeIdentity.Primitive(element, element == "string" ? CliValueKind.ManagedReference : CliValueKind.I4,
            element != "string");
        var holder = source.Add("Holder", unicode: unicode,
            fields: [("Items", CliTypeIdentity.SzArray(elementType), UnmanagedType.ByValArray)], sizeConst: 3, subtype: subtype);

        var field = NativeStructLayouts.PlanStructure(holder, source, 4, source).Layout?.Fields[0];

        Assert.Equal(kind, field?.Reference?.Kind.ToString());
        Assert.Equal(3 * elementSize, field?.Size ?? 0);
    }

    [Fact]
    public void ArraysOfClassesAndUnreadableStructsAreNotConverted()
    {
        var source = new NativeStructSource();
        var inner = source.Add("Inner", valueType: false, fields: [("X", Int32, null)]);
        var unknown = CliTypeIdentity.Named(Assembly, "Native", "Unknown", true);
        var classes = source.Add("Classes", fields: [("Items", CliTypeIdentity.SzArray(inner), UnmanagedType.ByValArray)], sizeConst: 2);
        var unknowns = source.Add("Unknowns", fields: [("Items", CliTypeIdentity.SzArray(unknown), UnmanagedType.ByValArray)], sizeConst: 2);

        Assert.All(new[] { classes, unknowns }, type =>
            Assert.Equal((null, false), (NativeStructLayouts.PlanStructure(type, source, 4, source).Layout,
                NativeStructLayouts.PlanStructure(type, source, 4, source).Rejected)));
    }

    // A formatted class whose base is not readable here is not converted, and is not known to be invalid.
    [Fact]
    public void AFormattedClassOnAnUnreadableBaseIsNotConverted()
    {
        var source = new NativeStructSource();
        var derived = source.Add("Derived", valueType: false, fields: [("X", Int32, null)]);
        source.Bases[derived] = CliTypeIdentity.Named(new AssemblyIdentity("Other"), "Other", "Base", false);

        var plan = NativeStructLayouts.PlanStructure(derived, source, 4, source);

        Assert.Equal((null, false, false), (plan.Layout, plan.Rejected, plan.InvalidBase));
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
