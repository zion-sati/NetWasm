using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// The signatures next to the ones CoreLib's vectors declare: each lowers only in
// its exact shape and otherwise keeps upstream's software implementation.
public sealed class SimdVectorIntrinsicClassifierTests
{
    private static readonly AssemblyIdentity Core = new("NetWasm.CoreLib");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
    private static readonly CliTypeIdentity Char = CliTypeIdentity.Primitive("char", CliValueKind.I4);
    private static readonly CliTypeIdentity NativeInt = CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt);
    private static readonly CliTypeIdentity NativeUInt = CliTypeIdentity.Primitive("nativeuint", CliValueKind.NativeInt);
    private static readonly CliTypeIdentity Bool = CliTypeIdentity.Primitive("bool", CliValueKind.I4);
    private static readonly CliTypeIdentity Void = CliTypeIdentity.FromStackKind(CliValueKind.Void);
    private static readonly CliTypeIdentity T = CliTypeIdentity.GenericParameter(method: true, 0);
    private static readonly CliTypeIdentity Vector = Vector128Of(Int32);
    private static readonly CliTypeIdentity ByRef = CliTypeIdentity.ManagedByReference(T);
    private static readonly CliTypeIdentity Pointer = CliTypeIdentity.UnmanagedPointer(T);
    private static readonly CliTypeIdentity Single = CliTypeIdentity.Primitive("f4", CliValueKind.F4);
    private static readonly CliTypeIdentity Floats = Vector128Of(Single);
    private static readonly CliTypeIdentity Vector2 = Numerics("Vector2");
    private static readonly CliTypeIdentity Vector3 = Numerics("Vector3");
    private static readonly CliTypeIdentity Plane = Numerics("Plane");
    private static readonly CliTypeIdentity Matrix3x2 = Numerics("Matrix3x2");

    private const string Vector128 = "System.Runtime.Intrinsics.Vector128";
    private const string Vector128OfT = "System.Runtime.Intrinsics.Vector128`1";

    public static TheoryData<string, string, bool, int, CliTypeIdentity, CliTypeIdentity[], RuntimeIntrinsic?> Shapes() => new()
    {
        { Vector128OfT, "get_Item", false, 0, Int32, [Int32], RuntimeIntrinsic.SimdGetElement },
        { Vector128OfT, "get_Item", false, 0, Int32, [Int64], null },
        { Vector128OfT, "GetHashCode", false, 0, Int32, [], null },
        { Vector128, "get_Item", false, 0, Int32, [Int32], null },
        { "System.Runtime.Intrinsics.Vector64", "Add", true, 0, Vector, [Vector, Vector], null },
        // A generic instantiation that is no vector, such as Span<int>.
        {
            Vector128, "Add", true, 0, Vector,
            [Vector, CliTypeIdentity.GenericInstantiation(CliTypeIdentity.Named(Core, "System", "Span`1", isValueType: true), [Int32])],
            null
        },
        { Vector128, "Create", true, 0, Vector, [Int32], RuntimeIntrinsic.SimdCreateBroadcast },
        { Vector128, "Create", true, 0, Vector, [Int32, Int32, Int32, Int32], RuntimeIntrinsic.SimdCreateElements },
        { Vector128, "Create", true, 0, Vector, [Int32, Int32], null },
        { Vector128, "Create", true, 0, Vector, [], null },
        { Vector128, "Create", true, 0, Int32, [Int32], null },
        { Vector128, "Create", true, 0, Vector, [Int64], null },
        { Vector128, "Create", true, 0, Vector128Of(Char), [Char], null },
        { Vector128, "Create", true, 0, Vector128Of(NativeInt), [NativeInt], RuntimeIntrinsic.SimdCreateBroadcast },
        { Vector128, "Create", true, 0, Vector128Of(NativeInt), [NativeInt, NativeInt], null },
        { Vector128, "LoadUnsafe", true, 1, Vector, [ByRef], RuntimeIntrinsic.SimdLoad },
        { Vector128, "LoadUnsafe", true, 1, Vector, [ByRef, NativeUInt], RuntimeIntrinsic.SimdLoad },
        { Vector128, "LoadUnsafe", true, 1, Vector, [ByRef, Int32], null },
        { Vector128, "LoadUnsafe", true, 1, Vector, [Pointer], null },
        { Vector128, "LoadUnsafe", true, 1, Vector, [ByRef, NativeUInt, NativeUInt], null },
        { Vector128, "Load", true, 1, Vector, [Pointer, NativeUInt], null },
        { Vector128, "LoadUnsafe", true, 0, Vector, [ByRef], null },
        { Vector128, "StoreUnsafe", true, 1, Void, [Vector, ByRef], RuntimeIntrinsic.SimdStore },
        { Vector128, "StoreUnsafe", true, 1, Void, [Vector, ByRef, NativeUInt], RuntimeIntrinsic.SimdStore },
        { Vector128, "StoreUnsafe", true, 1, Void, [Vector, ByRef, Int32], null },
        { Vector128, "StoreUnsafe", true, 1, Void, [Int32, ByRef], null },
        { Vector128, "StoreUnsafe", true, 1, Void, [Vector, Pointer], null },
        { Vector128, "StoreUnsafe", true, 1, Void, [Vector], null },
        { Vector128, "StoreUnsafe", true, 1, Vector, [Vector, ByRef], null },
        { Vector128, "Store", true, 1, Void, [Vector, Pointer, NativeUInt], null },
        { Vector128, "Abs", true, 0, Vector, [Int32], null },
        { Vector128, "Abs", true, 0, Int32, [Vector], null },
        { Vector128, "Add", true, 0, Vector, [Vector, Int32], null },
        { Vector128, "Add", true, 0, Int32, [Vector, Vector], null },
        { Vector128, "Divide", true, 0, Vector, [Vector, Int32], RuntimeIntrinsic.SimdDivide },
        { Vector128, "Divide", true, 0, Int32, [Vector, Int32], null },
        { Vector128, "Multiply", true, 0, Vector, [Int32, Vector], RuntimeIntrinsic.SimdMultiply },
        { Vector128, "Multiply", true, 0, Int32, [Int32, Vector], null },
        { Vector128, "Multiply", true, 0, Vector, [Int32, Int32], null },
        { Vector128, "MultiplyAddEstimate", true, 0, Vector, [Vector, Vector, Int32], null },
        { Vector128, "MultiplyAddEstimate", true, 0, Int32, [Vector, Vector, Vector], null },
        { Vector128, "EqualsAll", true, 0, Int32, [Vector, Vector], null },
        { Vector128, "EqualsAll", true, 0, Bool, [Vector, Int32], null },
        { Vector128, "ShiftLeft", true, 0, Vector, [Vector, Int64], null },
        { Vector128, "ShiftLeft", true, 0, Int32, [Vector, Int32], null },
        { Vector128OfT, "op_LeftShift", true, 0, Vector, [Vector, Int32], RuntimeIntrinsic.SimdShiftLeft },
        { Vector128OfT, "op_LeftShift", true, 0, Vector, [Int32, Int32], null },
        { Vector128, "ConditionalSelect", true, 0, Vector, [Vector, Vector, Int32], null },
        { Vector128, "Shuffle", true, 0, Vector, [Vector, Int32], null },
        { Vector128, "ExtractMostSignificantBits", true, 1, Int32, [Vector], null },
        { Vector128, "Sum", true, 1, Int32, [Vector], null },
        { Vector128, "GetElement", true, 1, Int32, [Vector, Int32], null },
        { Vector128, "ToScalar", true, 1, Int32, [Vector], null },
        { Vector128, "WithElement", true, 1, Vector, [Vector, Int32, T], RuntimeIntrinsic.SimdWithElement },
        { Vector128, "WithElement", true, 1, Vector, [Vector, Int64, T], null },
        { Vector128, "WithElement", true, 1, Int32, [Vector, Int32, T], null },
        { Vector128, "WithElement", true, 0, Vector, [Vector, Int32, Int32], null },
        // Lanes of two vectors in a fixed order, and one scalar in lane 0.
        { Vector128, "ZipLower", true, 1, Vector, [Vector, Vector], RuntimeIntrinsic.SimdPermute },
        { Vector128, "ConcatUpperLower", true, 1, Vector, [Vector, Vector], RuntimeIntrinsic.SimdPermute },
        { "System.Numerics.Vector", "UnzipOdd", true, 1, VectorOf(Int32), [VectorOf(Int32), VectorOf(Int32)], RuntimeIntrinsic.SimdPermute },
        { Vector128, "UnzipEven", true, 1, Vector, [Vector, Int32], null },
        { Vector128, "Zip", true, 1, Vector, [Vector, Vector], null },
        { Vector128, "CreateScalar", true, 1, Vector, [T], RuntimeIntrinsic.SimdCreateScalar },
        { Vector128, "CreateScalarUnsafe", true, 0, Floats, [Single], RuntimeIntrinsic.SimdCreateScalar },
        { Vector128, "CreateScalar", true, 0, Floats, [Int32], null },
        { Vector128, "CreateScalar", true, 1, Vector, [T, T], null },
        { Vector128, "CreateScalar", true, 2, Vector, [T], null },
        { Vector128, "CreateScalar", true, 0, Vector2, [Single], null },
        // The views of System.Numerics' float vectors, upstream's [Intrinsic] bridges.
        { Vector128, "AsVector128", true, 0, Floats, [Vector2], RuntimeIntrinsic.NumericsToVector128 },
        { Vector128, "AsVector128Unsafe", true, 0, Floats, [Vector3], RuntimeIntrinsic.NumericsToVector128 },
        { Vector128, "AsVector128", true, 0, Floats, [Plane], RuntimeIntrinsic.NumericsToVector128 },
        { Vector128, "AsVector128", true, 0, Floats, [Matrix3x2], null },
        { Vector128, "AsVector128", true, 0, Int32, [Vector2], null },
        { Vector128, "AsVector128", true, 1, Floats, [Vector2], null },
        { Vector128, "AsVector2", true, 0, Vector2, [Floats], RuntimeIntrinsic.NumericsFromVector128 },
        { Vector128, "AsVector3", true, 0, Vector3, [Floats], RuntimeIntrinsic.NumericsFromVector128 },
        { Vector128, "AsPlane", true, 0, Plane, [Floats], RuntimeIntrinsic.NumericsFromVector128 },
        { Vector128, "AsVector2", true, 0, Vector2, [Vector2], null },
        { Vector128, "AsVector2", true, 0, Vector2, [Floats, Floats], null },
        { "System.Numerics.Vector", "AsVector128", true, 0, Floats, [Vector2], null },
        // Vector<T> moves as a v128 as Vector128<T> does.
        { "System.Numerics.Vector", "Add", true, 0, VectorOf(Int32), [VectorOf(Int32), VectorOf(Int32)], RuntimeIntrinsic.SimdAdd },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ClassifiesOnlyTheExactShape(
        string declaringType, string name, bool isStatic, int genericArity,
        CliTypeIdentity result, CliTypeIdentity[] parameters, RuntimeIntrinsic? expected)
    {
        var signature = MethodSignatureModel.Create(result, parameters);
        var method = new MethodDefinitionModel(new(Core, 0x06000001), new(Core, 0x02000001), name, isStatic, signature, 1)
        {
            GenericArity = genericArity,
        };

        Assert.Equal(expected, SimdVectorIntrinsicClassifier.Classify(declaringType, method));
    }

    private static CliTypeIdentity Vector128Of(CliTypeIdentity element) => CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(Core, "System.Runtime.Intrinsics", "Vector128`1", isValueType: true), [element]);

    private static CliTypeIdentity Numerics(string name) =>
        CliTypeIdentity.Named(Core, "System.Numerics", name, isValueType: true);

    private static CliTypeIdentity VectorOf(CliTypeIdentity element) => CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(Core, "System.Numerics", "Vector`1", isValueType: true), [element]);
}
