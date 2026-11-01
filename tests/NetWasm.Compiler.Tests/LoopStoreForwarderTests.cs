using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Tests;

// The element shapes CIL allows that C# reaches only through inlining: a
// narrow element whose cached value its loads extend, nested loops over one
// element, and the bases, indices and addresses that keep a loop reading
// memory. LoopStoreForwardingCompilationTests covers the shapes C# writes,
// against desktop .NET.
public sealed class LoopStoreForwarderTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
    private static readonly CliTypeIdentity Char = CliTypeIdentity.Primitive("char", CliValueKind.I4);

    // Two counters and an index.
    private static readonly ImmutableArray<CliTypeIdentity> Locals = [Int32, Int32, Int32];

    public static TheoryData<string> ForwardedShapes() =>
        ["element updated in a loop", "char element updated in a loop", "nested loops over one element"];

    [Theory]
    [MemberData(nameof(ForwardedShapes))]
    public void ShapeKeepsTheElementInALocal(string shape)
    {
        var body = Shape(shape);

        var rewritten = Forwarder().Rewrite(body);

        Assert.NotSame(body, rewritten);
        Assert.True(rewritten.Locals.Length > body.Locals.Length);
        Validator().Validate(new ControlFlowGraphBuilderFactory().Create().Build(rewritten));
    }

    public static TheoryData<string> MemoryShapes() =>
    [
        "array argument reassigned in the loop", "index written after the entry access",
        "element of another width through its address", "address carried into the loop",
    ];

    [Theory]
    [MemberData(nameof(MemoryShapes))]
    public void ShapeKeepsReadingMemory(string shape)
    {
        var body = Shape(shape);

        Assert.Same(body, Forwarder().Rewrite(body));
    }

    private static CilMethodBody Shape(string shape) => shape switch
    {
        "element updated in a loop" => Body(code => code
            .SetElement(0, Int32, Zero)
            .Loop(0, inner => inner.BumpElement(0, Int32, Zero))),
        "char element updated in a loop" => Body(code => code
            .SetElement(2, Char, Zero)
            .Loop(0, inner => inner.BumpElement(2, Char, Zero))),
        "nested loops over one element" => Body(code => code
            .SetElement(0, Int32, Zero)
            .Loop(0, outer => outer.SetElement(0, Int32, Zero).Loop(1, inner => inner.BumpElement(0, Int32, Zero)))),
        "array argument reassigned in the loop" => Body(code => code
            .SetElement(0, Int32, Zero)
            .Loop(0, inner => inner.BumpElement(0, Int32, Zero).Local(CilOperation.LoadArgument, 0)
                .Local(CilOperation.StoreArgument, 0))),
        // index = i; a[index] = 0; index = i + 1; then a[index] += 1 in the loop.
        "index written after the entry access" => Body(code => code
            .Local(CilOperation.LoadArgument, 3).Local(CilOperation.StoreLocal, 2)
            .SetElement(0, Int32, Index)
            .Local(CilOperation.LoadArgument, 3).Op(CilOperation.LoadInt32, 1).Op(CilOperation.Add).Local(CilOperation.StoreLocal, 2)
            .Loop(0, inner => inner.BumpElement(0, Int32, Index))),
        // Unsafe.As<long, int>(ref b[0]) += 1.
        "element of another width through its address" => Body(code => code
            .Loop(0, inner => inner.Local(CilOperation.LoadArgument, 1).Op(CilOperation.LoadInt32, 0)
                .Op(CilOperation.LoadArrayElementAddress, new CilOperand.TypeIdentity(Int64)).Bump(Int32))),
        // Each iteration leaves the address the next one updates through.
        _ => Body(code => code
            .Address()
            .Loop(0, inner => inner.Bump(Int32).Address())
            .Op(CilOperation.Pop)),
    };

    private static CilBuilder Zero(CilBuilder code) => code.Op(CilOperation.LoadInt32, 0);

    private static CilBuilder Index(CilBuilder code) => code.Local(CilOperation.LoadLocal, 2);

    private static LoopStoreForwarder Forwarder() => new(new ControlFlowGraphBuilderFactory().Create(), Validator(),
        new ControlFlowNaturalLoopAnalyzer(new ControlFlowDominanceAnalyzer()), new Program(), enabled: true);

    private static ITypedStackValidator Validator()
    {
        var program = new Program();
        return new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program);
    }

    // static void Run(int[] a, long[] b, char[] c, int i): the code, then return.
    private static CilMethodBody Body(Func<CilBuilder, CilBuilder> write)
    {
        var signature = MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void),
            CliTypeIdentity.SzArray(Int32), CliTypeIdentity.SzArray(Int64), CliTypeIdentity.SzArray(Char), Int32);
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Run", true, signature, 1);
        return new(method, 8, [.. Locals.Select(local => local.StackKind)], write(new CilBuilder()).Return().Build())
        {
            LocalSignatureTypes = Locals,
        };
    }

    private sealed class Program : ITypeRepository, IFieldRepository, IMethodRepository, IRuntimeIntrinsicRegistry
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => throw new NotSupportedException();

        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();

        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = default;
            return false;
        }
    }
}

// The element accesses the forwarder tests compose.
internal static class ElementCil
{
    extension(CilBuilder code)
    {
        // array[index] = 0.
        public CilBuilder SetElement(int array, CliTypeIdentity type, Func<CilBuilder, CilBuilder> index) =>
            index(code.Local(CilOperation.LoadArgument, array)).Op(CilOperation.LoadInt32, 0)
                .Op(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(type));

        // array[index] += 1, truncated to a char's width.
        public CilBuilder BumpElement(int array, CliTypeIdentity type, Func<CilBuilder, CilBuilder> index)
        {
            index(index(code.Local(CilOperation.LoadArgument, array)).Local(CilOperation.LoadArgument, array))
                .Op(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(type)).Op(CilOperation.LoadInt32, 1).Op(CilOperation.Add);
            if (type.CanonicalName == "primitive:char")
            {
                code.Op(CilOperation.ConvertNumeric, new CilOperand.NumericConversion(16, true, false, false, false));
            }
            return code.Op(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(type));
        }

        // *address += 1, through the dup a compound assignment makes.
        public CilBuilder Bump(CliTypeIdentity type) => code.Op(CilOperation.Duplicate)
            .Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(type)).Op(CilOperation.LoadInt32, 1).Op(CilOperation.Add)
            .Op(CilOperation.StoreObject, new CilOperand.TypeIdentity(type));

        // &a[0].
        public CilBuilder Address() => code.Local(CilOperation.LoadArgument, 0).Op(CilOperation.LoadInt32, 0)
            .Op(CilOperation.LoadArrayElementAddress, new CilOperand.TypeIdentity(CliTypeIdentity.Primitive("i4", CliValueKind.I4)));
    }
}
