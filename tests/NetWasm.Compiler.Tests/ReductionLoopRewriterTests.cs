using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// The CIL shapes the reduction rewriter folds and the ones it leaves, beyond
// what C# compiles to: a body with no fold, a local folded two ways, Math.Min
// and Max over comparison types that do and do not order the elements, an
// index offset outside the range it takes, and bounds that are not invariant
// expressions of constants, arguments, lengths and unwritten locals.
public sealed class ReductionLoopRewriterTests
{
    private const int Counter = 0;
    private const int Folded = 1;
    private const int Copy = 2;
    private const int Holder = 3;

    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity HolderType = CliTypeIdentity.Named(Assembly, "Fixture", "Holder", isValueType: true);

    public static TheoryData<string, bool> Shapes() => new()
    {
        { "xor", true },
        { "copy only", false },
        { "local folded two ways", false },
        { "maximum of booleans", false },
        { "maximum of truncated longs", false },
        { "maximum of bytes as shorts", false },
        { "maximum of ushorts as uints", true },
        { "maximum of longs as doubles", false },
        { "maximum of ints widened unsigned", false },
        { "maximum of uints widened to ulongs", true },
        { "maximum of widened ints as doubles", false },
        { "index offset below the range", false },
        { "bound through the counter", false },
        { "bound of a folded local", false },
        { "bound of a struct's method", false },
        { "bound with a multiplication", false },
        { "bound with a nop", false },
        { "address ending the bound", false },
        { "converted counter", false },
        { "counter plus one", false },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void FoldsOnlyAssociativeReductionsUnderAnInvariantBound(string shape, bool rewritten)
    {
        var body = Shape(shape);

        var result = new ReductionLoopRewriter(new OperandCalledMethodResolver(), new Helpers(), new Helpers(), enabled: true)
            .Rewrite(body);

        Assert.Equal(rewritten, !ReferenceEquals(body, result));
    }

    private static CilMethodBody Shape(string shape) => shape switch
    {
        "xor" => Loop(code => code.Fold(Int32, CilOperation.BitwiseXor)),
        "copy only" => Loop(code => code.Element(Int32).Local(CilOperation.StoreLocal, Copy)),
        "local folded two ways" => Loop(code => code.Fold(Int32, CilOperation.BitwiseXor).Fold(Int32, CilOperation.Add)),
        "maximum of booleans" => Loop(code => code.Maximum(Primitive("bool", CliValueKind.I4), Int32)),
        "maximum of truncated longs" => Loop(code => code.Maximum(Primitive("i8", CliValueKind.I8), Int32, CilOperation.ConvertInt32)),
        "maximum of bytes as shorts" => Loop(code => code.Maximum(Primitive("u1", CliValueKind.I4), Primitive("i2", CliValueKind.I4))),
        "maximum of ushorts as uints" => Loop(code => code.Maximum(Primitive("u2", CliValueKind.I4), Primitive("u4", CliValueKind.I4))),
        "maximum of longs as doubles" => Loop(code => code.Maximum(Primitive("i8", CliValueKind.I8), Primitive("f8", CliValueKind.F8))),
        "maximum of ints widened unsigned" => Loop(code => code.Maximum(Int32, Primitive("i8", CliValueKind.I8),
            CilOperation.ConvertInt64Unsigned)),
        "maximum of uints widened to ulongs" => Loop(code => code.Maximum(Primitive("u4", CliValueKind.I4),
            Primitive("u8", CliValueKind.I8), CilOperation.ConvertInt64Unsigned)),
        "maximum of widened ints as doubles" => Loop(code => code.Maximum(Int32, Primitive("f8", CliValueKind.F8),
            CilOperation.ConvertInt64)),
        "index offset below the range" => Loop(code => code
            .Local(CilOperation.LoadLocal, Folded).Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, Counter)
            .Op(CilOperation.LoadInt32, -70_000).Op(CilOperation.Add)
            .Op(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Int32))
            .Op(CilOperation.BitwiseXor).Local(CilOperation.StoreLocal, Folded)),
        "bound through the counter" => Loop(Xor, bound => bound
            .Local(CilOperation.LoadArgument, 2).Local(CilOperation.LoadLocal, Counter).Op(CilOperation.Subtract)),
        "bound of a folded local" => Loop(Xor, bound => bound.Local(CilOperation.LoadLocal, Folded)),
        "bound of a struct's method" => Loop(Xor, bound => bound
            .Local(CilOperation.LoadLocalAddress, Holder).Op(CilOperation.Call, new CilOperand.MethodInstance(Get))),
        "bound with a multiplication" => Loop(Xor, bound => bound
            .Local(CilOperation.LoadArgument, 2).Op(CilOperation.LoadInt32, 2).Op(CilOperation.Multiply)),
        "bound with a nop" => Loop(Xor, bound => bound.Local(CilOperation.LoadArgument, 2).Op(CilOperation.Nop)),
        "address ending the bound" => Loop(Xor, bound => bound.Local(CilOperation.LoadArgumentAddress, 2)),
        "converted counter" => Loop(Xor, bound => bound.Op(CilOperation.ConvertInt32).Local(CilOperation.LoadArgument, 2)),
        // for (...; i + 1 < n; ...)
        _ => Loop(Xor, bound => bound.Op(CilOperation.LoadInt32, 1).Op(CilOperation.Add).Local(CilOperation.LoadArgument, 2)),
    };

    private static CilBuilder Xor(CilBuilder code) => code.Fold(Int32, CilOperation.BitwiseXor);

    // Holder.Get(), an instance method of a struct local.
    private static readonly MethodInstanceModel Get = new(
        new MethodDefinitionModel(new(Assembly, 0x06000010), new(Assembly, 0x02000003), "Get", false,
            new MethodSignatureModel(Int32, []), 1),
        HolderType, [], new MethodSignatureModel(Int32, []));

    // static void Run(int[] a, int[] b, int n): i = 0; goto test; start: body;
    // i = i + 1; test: if (i < bound) goto start; return. The bound is n unless
    // given.
    private static CilMethodBody Loop(Func<CilBuilder, CilBuilder> body, Func<CilBuilder, CilBuilder>? bound = null)
    {
        var code = new CilBuilder().Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, Counter)
            .Branch(CilOperation.Branch, "test").Label("start");
        body(code).Local(CilOperation.LoadLocal, Counter).Op(CilOperation.LoadInt32, 1).Op(CilOperation.Add)
            .Local(CilOperation.StoreLocal, Counter).Label("test").Local(CilOperation.LoadLocal, Counter);
        (bound ?? (static bound => bound.Local(CilOperation.LoadArgument, 2)))(code);
        var instructions = code.Branch(CilOperation.BranchIfLessThanSigned, "start").Return().Build();
        var array = CliTypeIdentity.SzArray(Int32);
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Run", true,
            new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.Void), [array, array, Int32]), 1);
        return new(method, 8, [CliValueKind.I4, CliValueKind.I4, CliValueKind.I4, CliValueKind.ValueType], instructions)
        {
            LocalSignatureTypes = [Int32, Int32, Int32, HolderType],
        };
    }

    private static CliTypeIdentity Primitive(string name, CliValueKind kind) => CliTypeIdentity.Primitive(name, kind);

    private sealed class Helpers : IArrayLoopMethodResolver, ISpanLengthResolver
    {
        public MethodInstanceModel Resolve(string name, CliTypeIdentity element) => Method(name, element);

        public MethodInstanceModel Resolve(MethodInstanceModel indexer) => Method("get_Length", Int32);

        private static MethodInstanceModel Method(string name, CliTypeIdentity type)
        {
            var signature = new MethodSignatureModel(type, []);
            var owner = CliTypeIdentity.Named(Assembly, "System.Runtime.CompilerServices", "ArrayLoops", isValueType: false);
            return new(new MethodDefinitionModel(new(Assembly, 0x06000020), new(Assembly, 0x02000004), name, true, signature, 1),
                owner, [], signature);
        }
    }

    private sealed class OperandCalledMethodResolver : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance method ? method.Value : null;
    }
}

// The folds the reduction rewriter tests compose, over argument 0's elements at local 0.
internal static class ReductionCil
{
    extension(CilBuilder code)
    {
        // a[i]
        public CilBuilder Element(CliTypeIdentity element) => code.Local(CilOperation.LoadArgument, 0)
            .Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(element));

        // x = x op a[i]
        public CilBuilder Fold(CliTypeIdentity element, CilOperation operation) => code.Local(CilOperation.LoadLocal, 1)
            .Element(element).Op(operation).Local(CilOperation.StoreLocal, 1);

        // x = Math.Max(x, [conversion] a[i]) over the comparison type.
        public CilBuilder Maximum(CliTypeIdentity element, CliTypeIdentity comparison, CilOperation? conversion = null)
        {
            code.Local(CilOperation.LoadLocal, 1).Element(element);
            if (conversion is { } widening) code.Op(widening);
            var signature = new MethodSignatureModel(comparison, [comparison, comparison]);
            var math = CliTypeIdentity.Named(new("System.Runtime"), "System", "Math", isValueType: false);
            var max = new MethodInstanceModel(
                new MethodDefinitionModel(new(new("System.Runtime"), 0x06000030), new(new("System.Runtime"), 0x02000005), "Max", true, signature, 1),
                math, [], signature);
            return code.Op(CilOperation.Call, new CilOperand.MethodInstance(max)).Local(CilOperation.StoreLocal, 1);
        }
    }
}
