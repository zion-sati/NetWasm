using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

// Each PackedSimd overload is one Wasm SIMD instruction, found by its name and
// the shapes of its vector, pointer and scalar parameters; any other shape is
// not one of upstream's overloads.
public sealed class PackedSimdInstructionsTests
{
    private static readonly AssemblyIdentity Core = new("NetWasm.CoreLib");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity NativeInt = CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt);
    private static readonly CliTypeIdentity Owner = CliTypeIdentity.Named(Core, "Fixture", "Owner", isValueType: true);

    [Fact]
    public void FindsAnOverloadByNameAndParameterShapes()
    {
        Assert.True(PackedSimdInstructions.TryGet(Method("Add", true, 0, Vector(Int32), Vector(Int32)), out var add));
        Assert.Equal("i32x4.add", add.Name);
        Assert.True(PackedSimdInstructions.TryGet(Method("LoadScalarAndInsert", true, 0, CliTypeIdentity.UnmanagedPointer(Int32),
            Vector(Int32), CliTypeIdentity.Primitive("u1", CliValueKind.I4)), out _));
        Assert.True(PackedSimdInstructions.TryGet(Method("ReplaceScalar", true, 0, Vector(Int32), CliTypeIdentity.Primitive("u1", CliValueKind.I4), Int32), out _));
    }

    public static TheoryData<string> OtherShapes() =>
        ["instance", "generic", "by-reference parameter", "vector of a struct", "pointer to a struct", "unknown overload"];

    [Theory]
    [MemberData(nameof(OtherShapes))]
    public void AnyOtherShapeIsNoOverload(string shape)
    {
        var method = shape switch
        {
            "instance" => Method("Add", false, 0, Vector(Int32), Vector(Int32)),
            "generic" => Method("Add", true, 1, Vector(Int32), Vector(Int32)),
            "by-reference parameter" => Method("Add", true, 0, CliTypeIdentity.ManagedByReference(Int32), Vector(Int32)),
            "vector of a struct" => Method("Add", true, 0, Vector(Owner), Vector(Owner)),
            "pointer to a struct" => Method("LoadScalarAndSplat", true, 0, CliTypeIdentity.UnmanagedPointer(Owner)),
            _ => Method("Shuffle", true, 0, Vector(Int32), Vector(Int32), Vector(Int32), Vector(Int32)),
        };

        Assert.False(PackedSimdInstructions.TryGet(method, out _));
    }

    [Fact]
    public void NativeWidthOverloadsNameNintOrNuintElements()
    {
        Assert.True(PackedSimdInstructions.IsNativeWidth(MethodSignatureModel.Create(Vector(NativeInt), Vector(NativeInt))));
        Assert.True(PackedSimdInstructions.IsNativeWidth(MethodSignatureModel.Create(Vector(Int32), CliTypeIdentity.UnmanagedPointer(NativeInt))));
        Assert.False(PackedSimdInstructions.IsNativeWidth(MethodSignatureModel.Create(Vector(Int32), Int32)));
    }

    private static CliTypeIdentity Vector(CliTypeIdentity element) => CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(Core, "System.Runtime.Intrinsics", "Vector128`1", isValueType: true), [element]);

    private static MethodDefinitionModel Method(string name, bool isStatic, int arity, params CliTypeIdentity[] parameters) =>
        new(new(Core, 0x06000001), new(Core, 0x02000001), name, isStatic, MethodSignatureModel.Create(Vector(Int32), parameters), 1)
        {
            GenericArity = arity,
        };
}
