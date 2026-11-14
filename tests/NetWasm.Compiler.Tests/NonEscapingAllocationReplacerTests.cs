using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Allocations;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

// The object shapes CIL allows around a local allocation: a value dropped, a
// float field zeroed, a filtered handler, and the addresses, prefixes,
// branches, constructors and older objects that keep the allocation.
// AllocationScalarReplacementCompilationTests covers the shapes C# writes,
// against desktop .NET.
public sealed class NonEscapingAllocationReplacerTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly EntityKey BoxKey = new(Assembly, 0x02000001);
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Single = CliTypeIdentity.Primitive("f4", CliValueKind.F4);
    private static readonly CliTypeIdentity Box = CliTypeIdentity.Named(Assembly, "Fixture", "Box", isValueType: false);
    internal static readonly FieldInstanceModel BoxValue = Field(1, "Value", Int32);
    private static readonly FieldInstanceModel Ratio = Field(2, "Ratio", Single);

    // Box(int value) { Value = value; } and Box() { throw null; }.
    internal static readonly MethodInstanceModel BoxConstructor = Method(2, Int32);
    private static readonly MethodInstanceModel Throwing = Method(3);

    // A constructor whose body references something missing; its own analysis reports that.
    private static readonly MethodInstanceModel Undecodable = Method(4);

    public static TheoryData<string> ReplacedShapes() => ["fields read and the object dropped", "object used in a filtered try"];

    [Theory]
    [MemberData(nameof(ReplacedShapes))]
    public void ShapeNeedsNoAllocation(string shape)
    {
        var body = Shape(shape);

        var rewritten = Replacer().Rewrite(body);

        Assert.DoesNotContain(rewritten.Instructions, static instruction => instruction.Operation == CilOperation.NewObject);
        Validator().Validate(new ControlFlowGraphBuilderFactory().Create().Build(rewritten));
    }

    public static TheoryData<string> AllocatedShapes() =>
    [
        "address of the object's local", "older object read after a newer one", "volatile field read",
        "object read across a branch", "constructor that throws", "older object read in a filtered handler",
        "constructor that cannot be decoded",
    ];

    [Theory]
    [MemberData(nameof(AllocatedShapes))]
    public void ShapeKeepsItsAllocation(string shape)
    {
        var body = Shape(shape);

        Assert.Same(body, Replacer().Rewrite(body));
    }

    // Each field of the replaced object becomes a local, numbered in the fields' metadata order:
    // the replacer's map of them is ordered by the keys' hashes, which differ from one process to
    // the next, and the locals' numbering is the code the compiler emits.
    [Fact]
    public void FieldsBecomeLocalsInMetadataOrder()
    {
        var fields = Enumerable.Range(0, 8).Select(index => Field(0x10 + index, $"F{index}", Int32)).ToArray();
        int[] reads = [5, 2, 7, 0, 3, 6, 1, 4];
        var body = Body(code =>
        {
            code = code.New(0, 5);
            foreach (var read in reads)
            {
                code = code.Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadField, new CilOperand.FieldInstance(fields[read]))
                    .Op(CilOperation.Pop);
            }
            return code.ReadValue(0);
        });

        var rewritten = Replacer().Rewrite(body);

        // The constructor's argument, each field as read, then the result's Value.
        var loaded = rewritten.Instructions.Where(static instruction => instruction.Operation == CilOperation.LoadLocal)
            .Select(static instruction => ((CilOperand.Index)instruction.Operand).Value).ToArray();
        var fieldLocals = loaded[1..^1];
        Assert.Equal(Enumerable.Range(fieldLocals.Min(), fields.Length),
            reads.Zip(fieldLocals).OrderBy(static pair => pair.First).Select(static pair => pair.Second));
        Assert.True(loaded[^1] < fieldLocals.Min());
    }

    private static CilMethodBody Shape(string shape) => shape switch
    {
        "fields read and the object dropped" => Body(code => code
            .New(0, 5)
            .Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadField, new CilOperand.FieldInstance(Ratio)).Op(CilOperation.Pop)
            .Local(CilOperation.LoadLocal, 0).Op(CilOperation.Pop)
            .ReadValue(0)),
        // try { _ = box.Value; } catch when (true) { } return box.Value;
        "object used in a filtered try" => Body(code => code
            .New(0, 5)
            .Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadField, new CilOperand.FieldInstance(BoxValue)).Op(CilOperation.Pop)
            .Branch(CilOperation.Leave, "end")
            .Op(CilOperation.Pop).Op(CilOperation.LoadInt32, 1).Op(CilOperation.EndFilter)
            .Op(CilOperation.Pop).Branch(CilOperation.Leave, "end")
            .Label("end").ReadValue(0)) with
        { ExceptionRegions = [Filtered] },
        "address of the object's local" => Body(code => code
            .New(0, 5).Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Pop).ReadValue(0)),
        "older object read after a newer one" => Body(code => code
            .New(0, 1).New(1, 2).ReadValue(0)),
        "volatile field read" => Body(code => code
            .New(0, 5).Local(CilOperation.LoadLocal, 0).Op(CilOperation.Volatile)
            .Op(CilOperation.LoadField, new CilOperand.FieldInstance(BoxValue)).Return()),
        "object read across a branch" => Body(code => code
            .New(0, 5).Local(CilOperation.LoadLocal, 0).Branch(CilOperation.Branch, "next")
            .Label("next").Op(CilOperation.LoadField, new CilOperand.FieldInstance(BoxValue)).Return()),
        "constructor that throws" => Body(code => code
            .Op(CilOperation.NewObject, new CilOperand.MethodInstance(Throwing)).Local(CilOperation.StoreLocal, 0).ReadValue(0)),
        "constructor that cannot be decoded" => Body(code => code
            .Op(CilOperation.NewObject, new CilOperand.MethodInstance(Undecodable)).Local(CilOperation.StoreLocal, 0).ReadValue(0)),
        // var older = new Box(1); try { var newer = new Box(2); _ = newer.Value; }
        // catch when (true) { return older.Value; } return 0;
        _ => Body(code => code
            .New(0, 1)
            .New(1, 2).Local(CilOperation.LoadLocal, 1).Op(CilOperation.LoadField, new CilOperand.FieldInstance(BoxValue))
            .Op(CilOperation.Pop).Branch(CilOperation.Leave, "end")
            .Op(CilOperation.Pop).Op(CilOperation.LoadInt32, 1).Op(CilOperation.EndFilter)
            .Op(CilOperation.Pop).Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadField, new CilOperand.FieldInstance(BoxValue))
            .Local(CilOperation.StoreLocal, 2).Branch(CilOperation.Leave, "end")
            .Label("end").Local(CilOperation.LoadLocal, 2).Return()) with
        { ExceptionRegions = [OlderInHandler] },
    };

    // The try at 3 to 6, its filter at 7 to 9 and its handler at 10 and 11.
    private static readonly CilExceptionRegion Filtered = new(CilExceptionRegionKind.Filter, 3, 4, 10, 2, null, 7);

    // The try at 3 to 9, its filter at 10 to 12 and its handler at 13 to 17.
    private static readonly CilExceptionRegion OlderInHandler = new(CilExceptionRegionKind.Filter, 3, 7, 13, 5, null, 10);

    private static NonEscapingAllocationReplacer Replacer()
    {
        var program = new Program();
        return new(new ControlFlowGraphBuilderFactory().Create(), Validator(), program, program, program, program, program,
            new StackValueConsumerFinder(new StackEffectResolver(program)), enabled: true);
    }

    private static ITypedStackValidator Validator()
    {
        var program = new Program();
        return new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program);
    }

    // static int Run(): the code, with two boxes and an int.
    private static CilMethodBody Body(Func<CilBuilder, CilBuilder> write)
    {
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "Run", true,
            MethodSignatureModel.Create(Int32), 1);
        return new(method, 8, [CliValueKind.ManagedReference, CliValueKind.ManagedReference, CliValueKind.I4],
            write(new CilBuilder()).Build())
        {
            LocalSignatureTypes = [Box, Box, Int32],
        };
    }

    private static FieldInstanceModel Field(int token, string name, CliTypeIdentity type) =>
        new(new FieldDefinitionModel(new(Assembly, 0x04000000 + token), BoxKey, name, type.StackKind, false) { SignatureType = type },
            Box, type);

    private static MethodInstanceModel Method(int token, params CliTypeIdentity[] parameters)
    {
        var signature = MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), parameters);
        return new(new(new(Assembly, 0x06000000 + token), BoxKey, ".ctor", false, signature, 1), Box, [], signature);
    }

    private sealed class Program :
        ITypeRepository, IFieldRepository, IMethodRepository, IMethodBodyReader, ICalleeMethodBodyReader, ICalledMethodResolver, IFinalizerResolver,
        IMethodInitializationTriggerClassifier
    {
        public bool TryReadMethodBody(
            MethodDefinitionModel method,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CilMethodBody? body) =>
            new CalleeMethodBodyReader(this).TryReadMethodBody(method, out body);

        public bool TryReadMethodBody(
            MethodInstanceModel method,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CilMethodBody? body) =>
            new CalleeMethodBodyReader(this).TryReadMethodBody(method, out body);

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => new(key, "Fixture", "Box", false, [], []);

        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();

        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance method ? method.Value : null;

        public CilMethodBody ReadMethodBody(MethodInstanceModel method) => method == Undecodable
            ? throw new CompilerException(new(DiagnosticCode.MissingApi, "missing"))
            : new(method.Definition, 2, [],
            method == BoxConstructor
                ? new CilBuilder().Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadArgument, 1)
                    .Op(CilOperation.StoreField, new CilOperand.FieldInstance(BoxValue)).Return().Build()
                : new CilBuilder().Op(CilOperation.LoadNull).Op(CilOperation.Throw).Build());

        public CilMethodBody ReadMethodBody(MethodDefinitionModel method) => throw new NotSupportedException();

        public MethodInstanceModel? Resolve(CliTypeIdentity allocatedType) => null;

        public bool Classify(MethodDefinitionModel method) => false;
    }
}

// The allocations the replacer tests compose.
internal static class AllocationCil
{
    extension(CilBuilder code)
    {
        // local = new Box(value).
        public CilBuilder New(int local, int value) => code.Op(CilOperation.LoadInt32, value)
            .Op(CilOperation.NewObject, new CilOperand.MethodInstance(NonEscapingAllocationReplacerTests.BoxConstructor))
            .Local(CilOperation.StoreLocal, local);

        // return local.Value.
        public CilBuilder ReadValue(int local) => code.Local(CilOperation.LoadLocal, local)
            .Op(CilOperation.LoadField, new CilOperand.FieldInstance(NonEscapingAllocationReplacerTests.BoxValue)).Return();
    }
}
