using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Tests;

// The accesses through a primitive or vector local's address that the folder
// turns into the local's own load or store, and those it leaves alone.
// Compilation tests reach it through inlined receivers and out parameters.
public sealed class LocalAddressDereferenceFolderTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity UInt64 = CliTypeIdentity.Primitive("u8", CliValueKind.I8);
    private static readonly CliTypeIdentity Vector = CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(Assembly, "System.Runtime.Intrinsics", "Vector128`1", isValueType: true), [Int32]);
    private static readonly CliTypeIdentity Other = CliTypeIdentity.Named(Assembly, "Fixture", "Other", isValueType: true);
    private static readonly CliTypeIdentity Single = CliTypeIdentity.Primitive("f4", CliValueKind.F4);
    private static readonly CliTypeIdentity Vector4 = CliTypeIdentity.Named(Assembly, "System.Numerics", "Vector4", isValueType: true);
    private static readonly CliTypeIdentity Vector2 = CliTypeIdentity.Named(Assembly, "System.Numerics", "Vector2", isValueType: true);

    public static TheoryData<CliTypeIdentity, CilOperation> Zeros() => new()
    {
        { CliTypeIdentity.Primitive("i8", CliValueKind.I8), CilOperation.LoadInt64 },
        { CliTypeIdentity.Primitive("f4", CliValueKind.F4), CilOperation.LoadFloat32 },
        { CliTypeIdentity.Primitive("f8", CliValueKind.F8), CilOperation.LoadFloat64 },
        { CliTypeIdentity.Primitive("bool", CliValueKind.I4), CilOperation.LoadInt32 },
        { CliTypeIdentity.Primitive("i1", CliValueKind.I4), CilOperation.LoadInt32 },
        { CliTypeIdentity.Primitive("char", CliValueKind.I4), CilOperation.LoadInt32 },
        { CliTypeIdentity.Primitive("i2", CliValueKind.I4), CilOperation.LoadInt32 },
    };

    // initobj through the address stores the zero of the local's type.
    [Theory]
    [MemberData(nameof(Zeros))]
    public void ClearingAPrimitiveLocalStoresItsZero(CliTypeIdentity type, CilOperation zero)
    {
        var body = Body([type], code => code.Local(CilOperation.LoadLocalAddress, 0)
            .Op(CilOperation.InitializeObject, new CilOperand.TypeIdentity(type)).Return());

        var rewritten = Folder().Rewrite(body);

        Assert.Equal(zero, rewritten.Instructions[0].Operation);
        Assert.Equal(CilOperation.StoreLocal, rewritten.Instructions[1].Operation);
        Assert.Equal(new CilOperand.Index(0), rewritten.Instructions[1].Operand);
    }

    // A native integer's zero depends on the target, so its initobj stays.
    [Fact]
    public void ClearingANativeIntegerLocalStaysAStoreThroughItsAddress()
    {
        var native = CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt);
        var body = Body([native], code => code.Local(CilOperation.LoadLocalAddress, 0)
            .Op(CilOperation.InitializeObject, new CilOperand.TypeIdentity(native)).Return());

        Assert.Same(body, Folder().Rewrite(body));
    }

    // An inlined Int32 method reads the receiver's m_value through its address.
    [Fact]
    public void ReadingAPrimitivesValueFieldLoadsTheLocal()
    {
        var body = Body([Int32], code => code.Op(CilOperation.LoadInt32, 5).Local(CilOperation.StoreLocal, 0)
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadField, ValueField(Int32, Int32))
            .Op(CilOperation.Pop).Return());

        var rewritten = Folder().Rewrite(body);

        Assert.Equal(CilOperation.Nop, rewritten.Instructions[2].Operation);
        Assert.Equal(CilOperation.LoadLocal, rewritten.Instructions[3].Operation);
        Assert.Equal(new CilOperand.Index(0), rewritten.Instructions[3].Operand);
    }

    public static TheoryData<string> KeptAccesses() =>
    [
        "another type's field", "a field definition",
    ];

    // Only the local's own value field, as an instantiated field, is the local.
    [Theory]
    [MemberData(nameof(KeptAccesses))]
    public void OtherFieldReadsThroughTheAddressStay(string access)
    {
        var (local, field) = access switch
        {
            "another type's field" => (Int32, (CilOperand)ValueField(Other, Int32)),
            _ => (Int32, new CilOperand.Entity(ValueFieldKey)),
        };
        var body = Body([local], code => code.Local(CilOperation.LoadLocalAddress, 0)
            .Op(CilOperation.LoadField, field).Op(CilOperation.Pop).Return());

        Assert.Same(body, Folder().Rewrite(body));
    }

    // A vector local's field read right after its address reads the local's
    // lane: the address becomes the local's load and the field read stays.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadingAVectorsFieldJustAfterItsAddressLoadsTheLocal(bool numerics)
    {
        var (local, field) = numerics ? (Vector4, ValueField(Vector4, Single)) : (Vector, ValueField(Vector, UInt64));
        var body = Body([local], code => code.Local(CilOperation.LoadLocalAddress, 0)
            .Op(CilOperation.LoadField, field).Op(CilOperation.Pop).Return());

        var rewritten = Folder().Rewrite(body);

        Assert.Equal(CilOperation.LoadLocal, rewritten.Instructions[0].Operation);
        Assert.Equal(new CilOperand.Index(0), rewritten.Instructions[0].Operand);
        Assert.Equal(body.Instructions[1], rewritten.Instructions[1]);
    }

    // With another evaluation between them, the local's load would read the
    // value before it, while the field read sees it after.
    [Fact]
    public void ReadingAVectorsFieldAfterOtherWorkStays()
    {
        var body = Body([Vector4], code => code.Local(CilOperation.LoadLocalAddress, 0)
            .Op(CilOperation.LoadInt32, 1).Op(CilOperation.Pop)
            .Op(CilOperation.LoadField, ValueField(Vector4, Single)).Op(CilOperation.Pop).Return());

        Assert.Same(body, Folder().Rewrite(body));
    }

    // A view as wide as the local is the local; a Vector2 written into a
    // Vector4's low half keeps its upper half, so it stays a store to memory.
    [Theory]
    [InlineData("Vector4", true)]
    [InlineData("Vector128", true)]
    [InlineData("Vector2", false)]
    public void VectorViewsFoldOnlyAtTheLocalsWidth(string view, bool folds)
    {
        var type = view switch { "Vector4" => Vector4, "Vector128" => Vector, _ => Vector2 };
        var body = Body([Vector4], code => code.Local(CilOperation.LoadLocalAddress, 0)
            .Op(CilOperation.DefaultValue, new CilOperand.TypeIdentity(type))
            .Op(CilOperation.StoreObject, new CilOperand.TypeIdentity(type)).Return());

        var rewritten = Folder().Rewrite(body);

        Assert.Equal(folds ? CilOperation.StoreLocal : CilOperation.StoreObject, rewritten.Instructions[2].Operation);
    }

    private static readonly EntityKey ValueFieldKey = new(Assembly, 0x04000001);

    private static CilOperand.FieldInstance ValueField(CliTypeIdentity owner, CliTypeIdentity type) =>
        new(new FieldInstanceModel(Field(type), owner, type));

    private static FieldDefinitionModel Field(CliTypeIdentity type) =>
        new(ValueFieldKey, new(Assembly, 0x02000001), "m_value", type.StackKind, false) { SignatureType = type };

    private static LocalAddressDereferenceFolder Folder()
    {
        var program = new Program();
        return new(new ControlFlowGraphBuilderFactory().Create(),
            new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program),
            enabled: true);
    }

    private static CilMethodBody Body(CliTypeIdentity[] locals, Func<CilBuilder, CilBuilder> write)
    {
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000100), "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void), 1);
        return new(method, 8, [.. locals.Select(local => local.StackKind)], write(new CilBuilder()).Build())
        {
            LocalSignatureTypes = [.. locals],
        };
    }

    private sealed class Program : ITypeDefinitionResolver, IFieldRepository, ITypeRepository, IMethodRepository
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => throw new NotSupportedException();

        public FieldDefinitionModel GetField(EntityKey key) => Field(Int32);

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }
}
