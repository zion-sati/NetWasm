using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Tests;

// The struct-local shapes CIL allows that C# reaches only through inlining
// and Unsafe: an address dropped, stores the field's type converts, accesses
// through a field or whole value of another type, a struct of declared size,
// whole copies in and out of a loop that splits its locals, and the structs
// whose layout or fields keep them whole. StructScalarReplacementCompilationTests
// covers the shapes C# writes, against desktop .NET.
public sealed class StructLocalScalarReplacerTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
    private static readonly CliTypeIdentity SByte = CliTypeIdentity.Primitive("i1", CliValueKind.I4);
    private static readonly CliTypeIdentity Single = CliTypeIdentity.Primitive("f4", CliValueKind.F4);
    private static readonly CliTypeIdentity Double = CliTypeIdentity.Primitive("f8", CliValueKind.F8);
    private static readonly CliTypeIdentity NativeInt = CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt);
    private static readonly CliTypeIdentity Object = CliTypeIdentity.Named(Assembly, "System", "Object", isValueType: false);

    internal static readonly Struct Pair = new("Pair", 1, ("A", Int32), ("B", Int32));
    internal static readonly Struct Other = new("Other", 2, ("X", Int32));
    private static readonly Struct Mixed = new("Mixed", 3, ("F", Single), ("D", Double), ("S", SByte), ("N", NativeInt), ("R", Object));
    private static readonly Struct Sized = new("Sized", 4, ("A", Int32)) { DeclaredSize = 16 };

    public static TheoryData<string> SplitShapes() =>
    [
        "address popped", "floats stored to their fields", "int stored to an sbyte field",
        "sized struct read only by field", "element copy reading one field", "element copy reading a field's address",
        "element copy never read", "element copy copied to another split local", "element copy read whole outside its loop",
        "copy to a whole local outside the loop", "copy from a whole local outside the loop", "copy between whole locals",
        "two whole loads outside the loop", "mixed struct cleared", "reference field read through its address",
    ];

    [Theory]
    [MemberData(nameof(SplitShapes))]
    public void ShapeSplitsItsLocal(string shape)
    {
        var body = Shape(shape);

        var rewritten = Replacer().Rewrite(body);

        Assert.NotSame(body, rewritten);
        Assert.True(rewritten.LocalSignatureTypes.Length > body.LocalSignatureTypes.Length);
        Validate(rewritten);
        var expected = shape switch
        {
            "int stored to an sbyte field" => CilOperation.ConvertNumeric,
            _ => (CilOperation?)null,
        };
        if (expected is { } conversion)
        {
            Assert.Contains(rewritten.Instructions, instruction => instruction.Operation == conversion);
        }
    }

    public static TheoryData<string> WholeShapes() =>
    [
        "value popped", "field of another type", "field address of another type", "address stored to a native int field",
        "clear of another type", "whole load of another type", "sized struct read whole", "sized struct copied",
        "volatile copy through a reference", "whole value's copy stored", "field address read at another width",
        "field address popped", "field address cleared as a struct",
    ];

    [Theory]
    [MemberData(nameof(WholeShapes))]
    public void ShapeKeepsItsLocalWhole(string shape)
    {
        var body = Shape(shape);

        Assert.Same(body, Replacer().Rewrite(body));
    }

    public static TheoryData<string> WholeStructs() =>
    [
        "enum", "explicit layout", "inline array", "intrinsic vector", "numerics vector", "typed reference",
        "struct field", "modified field", "nine fields", "only static and literal fields",
    ];

    // A struct of these layouts or fields keeps its storage.
    [Theory]
    [MemberData(nameof(WholeStructs))]
    public void StructStaysWhole(string kind)
    {
        var shape = kind switch
        {
            "enum" => Pair with { IsEnum = true },
            "explicit layout" => Pair with { Layout = CliTypeLayoutKind.Explicit },
            "inline array" => Pair with { InlineArrayLength = 2 },
            "intrinsic vector" => Pair with { Namespace = "System.Runtime.Intrinsics", Name = "Vector128`1" },
            "numerics vector" => Pair with { Namespace = "System.Numerics", Name = "Vector2" },
            "typed reference" => Pair with { Namespace = "System", Name = "TypedReference" },
            "struct field" => new Struct("Outer", 5, ("Inner", Pair.Type)),
            "modified field" => new Struct("Volatile", 6, ("A", CliTypeIdentity.Modified(Int32,
                CliTypeIdentity.Named(Assembly, "System.Runtime.CompilerServices", "IsVolatile", isValueType: false), isRequired: true))),
            "nine fields" => new Struct("Wide", 7, [.. Enumerable.Range(0, StructLocalScalarReplacer.MaximumFields + 1)
                .Select(field => ($"F{field}", Int32))]),
            _ => new Struct("Statics", 8, ("A", Int32)) { StaticFields = true },
        };
        var body = Body([shape.Type], code => code
            .Local(CilOperation.LoadLocalAddress, 0)
            .Op(CilOperation.LoadFieldAddress, Field(shape, 0))
            .Op(CilOperation.Pop)
            .Return());

        Assert.Same(body, Replacer(shape).Rewrite(body));
    }

    private static CilMethodBody Shape(string shape) => shape switch
    {
        "address popped" => Body([Pair.Type], code => code
            .Store(0, Pair, 0, CilOperation.LoadInt32, 1)
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Pop)
            .Read(0, Pair, 0).Return()),
        "floats stored to their fields" => Body([Mixed.Type], code => code
            .Store(0, Mixed, 0, CilOperation.LoadFloat32, 1.5)
            .Store(0, Mixed, 1, CilOperation.LoadFloat64, 2.5)
            .Read(0, Mixed, 1).Op(CilOperation.ConvertInt32).Return()),
        "int stored to an sbyte field" => Body([Mixed.Type], code => code
            .Store(0, Mixed, 2, CilOperation.LoadInt32, 300)
            .Read(0, Mixed, 2).Return()),
        "sized struct read only by field" => Body([Sized.Type], code => code
            .Store(0, Sized, 0, CilOperation.LoadInt32, 1)
            .Read(0, Sized, 0).Return()),
        "element copy reading one field" => Body([Pair.Type], code => code
            .Element(0).Read(0, Pair, 1).Return()),
        "element copy reading a field's address" => Body([Pair.Type], code => code
            .Element(0)
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadFieldAddress, Field(Pair, 1))
            .Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(Int32)).Return()),
        "element copy never read" => Body([Pair.Type], code => code
            .Element(0).Store(0, Pair, 0, CilOperation.LoadInt32, 1)
            .Op(CilOperation.LoadInt32, 0).Return()),
        "element copy copied to another split local" => Body([Pair.Type, Pair.Type], code => code
            .Element(0).Read(0, Pair, 1).Op(CilOperation.Pop)
            .Local(CilOperation.LoadLocal, 0).Local(CilOperation.StoreLocal, 1)
            .Read(1, Pair, 0).Return()),
        "element copy read whole outside its loop" => Body([Pair.Type, Pair.Type, Int32], code => code
            .Element(0)
            .Local(CilOperation.LoadLocal, 0).Local(CilOperation.StoreLocal, 1)
            .Loop(2, inner => inner.Read(0, Pair, 0).Op(CilOperation.Pop))
            .Op(CilOperation.LoadInt32, 0).Return()),
        // Local 1's address escapes to an access of another type, so it stays whole.
        "copy to a whole local outside the loop" => Body([Pair.Type, Pair.Type, Int32], code => code
            .Store(0, Pair, 0, CilOperation.LoadInt32, 1)
            .Local(CilOperation.LoadLocal, 0).Local(CilOperation.StoreLocal, 1)
            .Escape(1)
            .Loop(2, inner => inner.Read(0, Pair, 0).Op(CilOperation.Pop))
            .Op(CilOperation.LoadInt32, 0).Return()),
        "copy from a whole local outside the loop" => Body([Pair.Type, Pair.Type, Int32], code => code
            .Escape(1)
            .Local(CilOperation.LoadLocal, 1).Local(CilOperation.StoreLocal, 0)
            .Loop(2, inner => inner.Read(0, Pair, 0).Op(CilOperation.Pop))
            .Op(CilOperation.LoadInt32, 0).Return()),
        // Locals 0 and 1 copy between themselves and stay whole beside local 2.
        "copy between whole locals" => Body([Pair.Type, Pair.Type, Pair.Type], code => code
            .Escape(0).Escape(1)
            .Local(CilOperation.LoadLocal, 0).Local(CilOperation.StoreLocal, 1)
            .Store(2, Pair, 0, CilOperation.LoadInt32, 1)
            .Read(2, Pair, 0).Return()),
        "two whole loads outside the loop" => Body([Pair.Type, Int32], code => code
            .Store(0, Pair, 0, CilOperation.LoadInt32, 1)
            .Local(CilOperation.LoadLocal, 0).Op(CilOperation.Pop).Local(CilOperation.LoadLocal, 0).Op(CilOperation.Pop)
            .Loop(1, inner => inner.Read(0, Pair, 0).Op(CilOperation.Pop))
            .Op(CilOperation.LoadInt32, 0).Return()),
        "mixed struct cleared" => Body([Mixed.Type], code => code
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.InitializeObject, new CilOperand.TypeIdentity(Mixed.Type))
            .Read(0, Mixed, 3).Op(CilOperation.ConvertInt32).Return()),
        "reference field read through its address" => Body([Mixed.Type], code => code
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadFieldAddress, Field(Mixed, 4))
            .Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(Object)).Op(CilOperation.Pop)
            .Op(CilOperation.LoadInt32, 0).Return()),
        // A deconstruction whose second copy is stored rather than read.
        "whole value's copy stored" => Body([Pair.Type, Pair.Type], code => code
            .Store(0, Pair, 0, CilOperation.LoadInt32, 1)
            .Local(CilOperation.LoadLocal, 0).Op(CilOperation.Duplicate).Op(CilOperation.LoadField, Field(Pair, 0))
            .Op(CilOperation.Pop).Local(CilOperation.StoreLocal, 1)
            .Read(0, Pair, 0).Return()),
        "field address read at another width" => Body([Pair.Type], code => code
            .Store(0, Pair, 0, CilOperation.LoadInt32, 1)
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadFieldAddress, Field(Pair, 0))
            .Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(Int64)).Op(CilOperation.ConvertInt32).Return()),
        "field address popped" => Body([Pair.Type], code => code
            .Store(0, Pair, 0, CilOperation.LoadInt32, 1)
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadFieldAddress, Field(Pair, 0)).Op(CilOperation.Pop)
            .Read(0, Pair, 0).Return()),
        // A field's address cleared as a whole struct clears past the field.
        "field address cleared as a struct" => Body([Pair.Type], code => code
            .Store(0, Pair, 0, CilOperation.LoadInt32, 1)
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadFieldAddress, Field(Pair, 0))
            .Op(CilOperation.InitializeObject, new CilOperand.TypeIdentity(Pair.Type))
            .Read(0, Pair, 0).Return()),
        "value popped" => Body([Pair.Type], code => code
            .Store(0, Pair, 0, CilOperation.LoadInt32, 1)
            .Local(CilOperation.LoadLocal, 0).Op(CilOperation.Pop)
            .Read(0, Pair, 0).Return()),
        // Unsafe.As inlined: the struct read as another.
        "field of another type" => Body([Pair.Type], code => code
            .Store(0, Pair, 0, CilOperation.LoadInt32, 1)
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadField, Field(Other, 0)).Return()),
        "field address of another type" => Body([Pair.Type], code => code
            .Store(0, Pair, 0, CilOperation.LoadInt32, 1)
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadFieldAddress, Field(Other, 0))
            .Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(Int32)).Return()),
        // A pinned address, as fixed stores it, which only the field keeps.
        "address stored to a native int field" => Body([Mixed.Type, Int32], code => code
            .Local(CilOperation.LoadLocalAddress, 0).Local(CilOperation.LoadLocalAddress, 1)
            .Op(CilOperation.StoreField, Field(Mixed, 3))
            .Read(0, Mixed, 3).Op(CilOperation.ConvertInt32).Return()),
        "clear of another type" => Body([Pair.Type], code => code
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.InitializeObject, new CilOperand.TypeIdentity(Other.Type))
            .Read(0, Pair, 0).Return()),
        "whole load of another type" => Body([Pair.Type], code => code
            .Store(0, Pair, 0, CilOperation.LoadInt32, 1)
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(Int64))
            .Op(CilOperation.ConvertInt32).Return()),
        "sized struct read whole" => Body([Sized.Type, Int32], code => code
            .Local(CilOperation.LoadLocal, 0).Op(CilOperation.Pop)
            .Loop(1, inner => inner.Store(0, Sized, 0, CilOperation.LoadInt32, 1))
            .Read(0, Sized, 0).Return()),
        "sized struct copied" => Body([Sized.Type, Sized.Type], code => code
            .Store(0, Sized, 0, CilOperation.LoadInt32, 1)
            .Local(CilOperation.LoadLocal, 0).Local(CilOperation.StoreLocal, 1)
            .Read(1, Sized, 0).Return()),
        _ => Body([Pair.Type], code => code
            .Local(CilOperation.LoadArgument, 1)
            .Op(CilOperation.Volatile).Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(Pair.Type))
            .Local(CilOperation.StoreLocal, 0)
            .Read(0, Pair, 0).Return()),
    };

    private static StructLocalScalarReplacer Replacer(params Struct[] structs)
    {
        var program = new Program([Pair, Other, Mixed, Sized, .. structs]);
        return new(new ControlFlowGraphBuilderFactory().Create(), Validator(program),
            new ControlFlowNaturalLoopAnalyzer(new ControlFlowDominanceAnalyzer()), program, program, enabled: true);
    }

    private static ITypedStackValidator Validator(Program program) =>
        new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program);

    private static void Validate(CilMethodBody body) =>
        Validator(new Program([])).Validate(new ControlFlowGraphBuilderFactory().Create().Build(body));

    // static int Run(Pair[] elements, ref Pair element): the code, with these locals.
    private static CilMethodBody Body(CliTypeIdentity[] locals, Func<CilBuilder, CilBuilder> write)
    {
        var signature = MethodSignatureModel.Create(Int32, CliTypeIdentity.SzArray(Pair.Type),
            CliTypeIdentity.ManagedByReference(Pair.Type));
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000100), "Run", true, signature, 1);
        return new(method, 8, [.. locals.Select(local => local.StackKind)], write(new CilBuilder()).Build())
        {
            LocalSignatureTypes = [.. locals],
        };
    }

    internal static CilOperand.FieldInstance Field(Struct owner, int field) => new(owner.Instance(field));

    internal sealed record Struct(string Name, int Token, params (string Name, CliTypeIdentity Type)[] Fields)
    {
        public string Namespace { get; init; } = "Fixture";
        public bool IsEnum { get; init; }
        public CliTypeLayoutKind Layout { get; init; } = CliTypeLayoutKind.Sequential;
        public int InlineArrayLength { get; init; }
        public int DeclaredSize { get; init; }
        public bool StaticFields { get; init; }

        public EntityKey Key => new(Assembly, 0x02000000 + Token);

        public CliTypeIdentity Type => CliTypeIdentity.Named(Assembly, "Fixture", Name, isValueType: true);

        public FieldDefinitionModel FieldDefinition(int field) =>
            new(new(Assembly, 0x04000000 + Token * 16 + field), Key, Fields[field].Name, Fields[field].Type.StackKind, StaticFields)
            {
                SignatureType = Fields[field].Type,
                IsLiteral = StaticFields && field == 0,
            };

        public FieldInstanceModel Instance(int field) => new(FieldDefinition(field), Type, Fields[field].Type);

        public TypeDefinitionModel Definition => new(Key, Namespace, Name, true,
            [.. Enumerable.Range(0, Fields.Length).Select(index => FieldDefinition(index).Key)], [])
        {
            IsEnum = IsEnum,
            LayoutKind = Layout,
            InlineArrayLength = InlineArrayLength,
            DeclaredSize = DeclaredSize,
        };
    }

    private sealed class Program(IEnumerable<Struct> structs) :
        ITypeDefinitionResolver, IFieldRepository, ITypeRepository, IMethodRepository
    {
        private readonly Struct[] _structs = [.. structs];

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            _structs.Last(candidate => candidate.Type.Equals(identity)).Definition;

        public FieldDefinitionModel GetField(EntityKey key) => _structs
            .SelectMany(candidate => Enumerable.Range(0, candidate.Fields.Length).Select(candidate.FieldDefinition))
            .Last(field => field.Key == key);

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }
}

// The struct accesses the tests compose.
internal static class StructLocalCil
{
    extension(CilBuilder code)
    {
        // local.field = value.
        public CilBuilder Store(int local, StructLocalScalarReplacerTests.Struct owner, int field, CilOperation load, double value) =>
            code.Local(CilOperation.LoadLocalAddress, local).Op(load, value)
                .Op(CilOperation.StoreField, StructLocalScalarReplacerTests.Field(owner, field));

        // local.field.
        public CilBuilder Read(int local, StructLocalScalarReplacerTests.Struct owner, int field) =>
            code.Local(CilOperation.LoadLocalAddress, local)
                .Op(CilOperation.LoadField, StructLocalScalarReplacerTests.Field(owner, field));

        // local = elements[0].
        public CilBuilder Element(int local) => code.Local(CilOperation.LoadArgument, 0).Op(CilOperation.LoadInt32, 0)
            .Op(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(StructLocalScalarReplacerTests.Pair.Type))
            .Local(CilOperation.StoreLocal, local);

        // The local's address read as another struct, which keeps it whole.
        public CilBuilder Escape(int local) => code.Local(CilOperation.LoadLocalAddress, local)
            .Op(CilOperation.InitializeObject, new CilOperand.TypeIdentity(StructLocalScalarReplacerTests.Other.Type));
    }
}
