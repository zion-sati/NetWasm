using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// The block copies the folder traces to two locals and a whole size, in bodies
// built here: addresses and lengths behind conversions, Unsafe's views, Unsafe.SizeOf
// and a temporary, and sizes a primitive or a field-less struct declares. Values
// that two paths produce, other calls, a temporary seen more than once, an
// instantiation's constant and a struct with instance fields stay copies. A folded
// copy's destination read later in its block reads the source, but not past a
// branch target or after the source is stored.
public sealed class WholeLocalBlockCopyFolderBodyTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
    private static readonly CliTypeIdentity Big = CliTypeIdentity.Named(Assembly, "Fixture", "Big", isValueType: true);
    private static readonly CliTypeIdentity Sized = CliTypeIdentity.Named(Assembly, "Fixture", "Sized", isValueType: true);
    private static readonly CliTypeIdentity Fielded = CliTypeIdentity.Named(Assembly, "Fixture", "Fielded", isValueType: true);
    private static readonly CliTypeIdentity Pair = CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(Assembly, "Fixture", "Pair`1", isValueType: true), [Int32]);
    private static readonly CliTypeIdentity UnsafeType =
        CliTypeIdentity.Named(Assembly, "System.Runtime.CompilerServices", "Unsafe", isValueType: false);
    private static readonly EntityKey SizedKey = new(Assembly, 0x02000010);
    private static readonly EntityKey FieldedKey = new(Assembly, 0x02000011);

    public static TheoryData<string, bool> Copies() => new()
    {
        { "sizeof through converted addresses", true },
        { "constant through a conversion", true },
        { "constant through an unchecked conversion", true },
        { "constant shorter than the local", false },
        { "destination from either branch", false },
        { "converted destination from either branch", false },
        { "viewed destination from either branch", false },
        { "length from either branch", false },
        { "converted length from either branch", false },
        { "addresses through AsRef", true },
        { "addresses through AsPointer", true },
        { "length behind an uncounted instruction", false },
        { "converted length behind an uncounted instruction", false },
        { "address through a one-type As", false },
        { "address through another method", false },
        { "Unsafe.SizeOf", true },
        { "Unsafe.SizeOf without its type", false },
        { "length through a temporary", true },
        { "temporary stored twice", false },
        { "temporary read twice", false },
        { "temporary whose address is taken", false },
        { "temporary read before its store", false },
        { "branch target between the temporary's store and load", false },
        { "temporary stored from either branch", false },
        { "constant for an instantiation", false },
        { "constant of a field-less struct's declared size", true },
        { "constant of a struct with an instance field", false },
    };

    [Theory]
    [MemberData(nameof(Copies))]
    public void FoldsTheCopiesItTracesToTwoLocalsAndAWholeSize(string shape, bool folded)
    {
        var body = Copy(shape);

        var rewritten = Folder().Rewrite(body);

        Assert.Equal(!folded, rewritten.Instructions.Any(static instruction => instruction.Operation == CilOperation.CopyBlock));
    }

    // After the folded copy, ldloc 0 then stloc 1, the destination is read once.
    [Theory]
    [InlineData("in the copy's block", false)]
    [InlineData("after a branch target", true)]
    [InlineData("after the source is stored", true)]
    public void AReadOfTheDestinationTakesTheSourceOnlyWithinTheBlockBeforeTheSourceChanges(string read, bool kept)
    {
        var body = Body([Big, Big, Big], code =>
        {
            Addresses(code).Op(CilOperation.SizeOf, new CilOperand.TypeIdentity(Big)).Op(CilOperation.CopyBlock);
            switch (read)
            {
                case "after a branch target":
                    code.Op(CilOperation.LoadArgument, new CilOperand.Index(0)).Branch(CilOperation.BranchIfTrue, "read")
                        .Label("read");
                    break;
                case "after the source is stored":
                    code.Local(CilOperation.LoadLocal, 2).Local(CilOperation.StoreLocal, 0);
                    break;
            }
            return code.Local(CilOperation.LoadLocal, 1).Op(CilOperation.Pop).Return();
        });

        var rewritten = Folder().Rewrite(body);

        Assert.DoesNotContain(rewritten.Instructions, static instruction => instruction.Operation == CilOperation.CopyBlock);
        Assert.Equal(kept, rewritten.Instructions.Any(static instruction =>
            instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: 1 } }));
        Assert.Equal(kept, rewritten.Instructions.Any(static instruction =>
            instruction is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: 1 } }));
    }

    // Local 1 is the destination, local 0 the source and local 2 an int temporary.
    private static CilMethodBody Copy(string shape) => shape switch
    {
        "sizeof through converted addresses" => Body([Big, Big, Int32], code => code
            .Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.ConvertNativeUInt)
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.ConvertNativeUInt)
            .Op(CilOperation.SizeOf, new CilOperand.TypeIdentity(Big)).Op(CilOperation.CopyBlock).Return()),
        "constant through a conversion" => Body([Int64, Int64, Int32], code => Addresses(code)
            .Op(CilOperation.LoadInt32, 8).Op(CilOperation.ConvertNativeUInt).Op(CilOperation.CopyBlock).Return()),
        "constant through an unchecked conversion" => Body([Int64, Int64, Int32], code => Addresses(code)
            .Op(CilOperation.LoadInt32, 8).Op(CilOperation.ConvertNumeric, new CilOperand.NumericConversion(32, true, false, false, false))
            .Op(CilOperation.CopyBlock).Return()),
        "constant shorter than the local" => Body([Int64, Int64, Int32], code => Addresses(code)
            .Op(CilOperation.LoadInt32, 4).Op(CilOperation.CopyBlock).Return()),
        "destination from either branch" => Body([Big, Big, Int32], code => Either(code,
                arm => arm.Local(CilOperation.LoadLocalAddress, 1))
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.SizeOf, new CilOperand.TypeIdentity(Big))
            .Op(CilOperation.CopyBlock).Return()),
        "converted destination from either branch" => Body([Big, Big, Int32], code => Either(code,
                arm => arm.Local(CilOperation.LoadLocalAddress, 1))
            .Op(CilOperation.ConvertNativeUInt).Local(CilOperation.LoadLocalAddress, 0)
            .Op(CilOperation.SizeOf, new CilOperand.TypeIdentity(Big)).Op(CilOperation.CopyBlock).Return()),
        "viewed destination from either branch" => Body([Big, Big, Int32], code => Either(code,
                arm => arm.Local(CilOperation.LoadLocalAddress, 1))
            .Op(CilOperation.Call, Method(UnsafeType, "AsRef", 1, [Big])).Local(CilOperation.LoadLocalAddress, 0)
            .Op(CilOperation.SizeOf, new CilOperand.TypeIdentity(Big)).Op(CilOperation.CopyBlock).Return()),
        "length from either branch" => Body([Int64, Int64, Int32], code => Either(Addresses(code),
                arm => arm.Op(CilOperation.LoadInt32, 8))
            .Op(CilOperation.CopyBlock).Return()),
        "converted length from either branch" => Body([Int64, Int64, Int32], code => Either(Addresses(code),
                arm => arm.Op(CilOperation.LoadInt32, 8))
            .Op(CilOperation.ConvertNativeUInt).Op(CilOperation.CopyBlock).Return()),
        "addresses through AsRef" => Body([Big, Big, Int32], code => code
            .Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.Call, Method(UnsafeType, "AsRef", 1, [Big]))
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Call, Method(UnsafeType, "AsRef", 1, [Big]))
            .Op(CilOperation.SizeOf, new CilOperand.TypeIdentity(Big)).Op(CilOperation.CopyBlock).Return()),
        "addresses through AsPointer" => Body([Big, Big, Int32], code => code
            .Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.Call, Method(UnsafeType, "AsPointer", 1, [Big]))
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Call, Method(UnsafeType, "AsPointer", 1, [Big]))
            .Op(CilOperation.SizeOf, new CilOperand.TypeIdentity(Big)).Op(CilOperation.CopyBlock).Return()),
        // localloc's effect on the stack is not counted, so no producer of the slot it leaves is found.
        "length behind an uncounted instruction" => Body([Int64, Int64, Int32], code => Addresses(code)
            .Op(CilOperation.LoadInt32, 8).Op(CilOperation.LocalAllocate).Op(CilOperation.CopyBlock).Return()),
        "converted length behind an uncounted instruction" => Body([Int64, Int64, Int32], code => Addresses(code)
            .Op(CilOperation.LoadInt32, 8).Op(CilOperation.LocalAllocate).Op(CilOperation.ConvertNativeUInt)
            .Op(CilOperation.CopyBlock).Return()),
        "address through a one-type As" => Body([Big, Big, Int32], code => code
            .Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.Call, Method(UnsafeType, "As", 1, [Big]))
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.SizeOf, new CilOperand.TypeIdentity(Big))
            .Op(CilOperation.CopyBlock).Return()),
        "address through another method" => Body([Big, Big, Int32], code => code
            .Local(CilOperation.LoadLocalAddress, 1)
            .Op(CilOperation.Call, Method(CliTypeIdentity.Named(Assembly, "Fixture", "Views", isValueType: false), "AsRef", 1, [Big]))
            .Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.SizeOf, new CilOperand.TypeIdentity(Big))
            .Op(CilOperation.CopyBlock).Return()),
        "Unsafe.SizeOf" => Body([Big, Big, Int32], code => Addresses(code)
            .Op(CilOperation.Call, SizeOf([Big])).Op(CilOperation.CopyBlock).Return()),
        "Unsafe.SizeOf without its type" => Body([Big, Big, Int32], code => Addresses(code)
            .Op(CilOperation.Call, SizeOf([])).Op(CilOperation.CopyBlock).Return()),
        "length through a temporary" => Body([Int64, Int64, Int32], code => Addresses(code
                .Op(CilOperation.LoadInt32, 8).Local(CilOperation.StoreLocal, 2))
            .Local(CilOperation.LoadLocal, 2).Op(CilOperation.CopyBlock).Return()),
        "temporary stored twice" => Body([Int64, Int64, Int32], code => Addresses(code
                .Op(CilOperation.LoadInt32, 8).Local(CilOperation.StoreLocal, 2)
                .Op(CilOperation.LoadInt32, 8).Local(CilOperation.StoreLocal, 2))
            .Local(CilOperation.LoadLocal, 2).Op(CilOperation.CopyBlock).Return()),
        "temporary read twice" => Body([Int64, Int64, Int32], code => Addresses(code
                .Op(CilOperation.LoadInt32, 8).Local(CilOperation.StoreLocal, 2).Local(CilOperation.LoadLocal, 2)
                .Op(CilOperation.Pop))
            .Local(CilOperation.LoadLocal, 2).Op(CilOperation.CopyBlock).Return()),
        "temporary whose address is taken" => Body([Int64, Int64, Int32], code => Addresses(code
                .Op(CilOperation.LoadInt32, 8).Local(CilOperation.StoreLocal, 2).Local(CilOperation.LoadLocalAddress, 2)
                .Op(CilOperation.Pop))
            .Local(CilOperation.LoadLocal, 2).Op(CilOperation.CopyBlock).Return()),
        "temporary read before its store" => Body([Int64, Int64, Int32], code => Addresses(code)
            .Local(CilOperation.LoadLocal, 2).Op(CilOperation.CopyBlock)
            .Op(CilOperation.LoadInt32, 8).Local(CilOperation.StoreLocal, 2).Return()),
        "branch target between the temporary's store and load" => Body([Int64, Int64, Int32], code => Addresses(code
                .Op(CilOperation.LoadInt32, 8).Local(CilOperation.StoreLocal, 2)
                .Op(CilOperation.LoadArgument, new CilOperand.Index(0)).Branch(CilOperation.BranchIfTrue, "copy").Label("copy"))
            .Local(CilOperation.LoadLocal, 2).Op(CilOperation.CopyBlock).Return()),
        "temporary stored from either branch" => Body([Int64, Int64, Int32], code => Addresses(Either(code,
                    arm => arm.Op(CilOperation.LoadInt32, 8))
                .Local(CilOperation.StoreLocal, 2))
            .Local(CilOperation.LoadLocal, 2).Op(CilOperation.CopyBlock).Return()),
        "constant for an instantiation" => Body([Pair, Pair, Int32], code => Addresses(code)
            .Op(CilOperation.LoadInt32, 8).Op(CilOperation.CopyBlock).Return()),
        "constant of a field-less struct's declared size" => Body([Sized, Sized, Int32], code => Addresses(code)
            .Op(CilOperation.LoadInt32, 16).Op(CilOperation.CopyBlock).Return()),
        "constant of a struct with an instance field" => Body([Fielded, Fielded, Int32], code => Addresses(code)
            .Op(CilOperation.LoadInt32, 16).Op(CilOperation.CopyBlock).Return()),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    // The destination's address, then the source's.
    private static CilBuilder Addresses(CilBuilder code) =>
        code.Local(CilOperation.LoadLocalAddress, 1).Local(CilOperation.LoadLocalAddress, 0);

    // if (argument 0) { arm } else { arm }: one value that two paths produce.
    private static CilBuilder Either(CilBuilder code, Func<CilBuilder, CilBuilder> arm) =>
        arm(arm(code.Op(CilOperation.LoadArgument, new CilOperand.Index(0)).Branch(CilOperation.BranchIfTrue, "second"))
            .Branch(CilOperation.Branch, "joined").Label("second"))
            .Label("joined");

    private static CilOperand.MethodInstance Method(CliTypeIdentity owner, string name, int arity, ImmutableArray<CliTypeIdentity> arguments)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.ManagedAddress, CliValueKind.ManagedAddress);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000010), new(Assembly, 0x02000020), name, true, signature, 1)
        {
            GenericArity = arity,
        };
        return new(new MethodInstanceModel(definition, owner, arguments, signature));
    }

    private static CilOperand.MethodInstance SizeOf(ImmutableArray<CliTypeIdentity> arguments)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000011), new(Assembly, 0x02000020), "SizeOf", true, signature, 1)
        {
            GenericArity = 1,
        };
        return new(new MethodInstanceModel(definition, UnsafeType, arguments, signature));
    }

    private static WholeLocalBlockCopyFolder Folder()
    {
        var program = new Program();
        return new(new ControlFlowGraphBuilderFactory().Create(),
            new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program),
            new OperandCalledMethodResolver(), program, program, enabled: true);
    }

    private static CilMethodBody Body(CliTypeIdentity[] locals, Func<CilBuilder, CilBuilder> write)
    {
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000100), "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4), 1);
        return new(method, 8, [.. locals.Select(static local => local.StackKind)], write(new CilBuilder()).Build())
        {
            LocalSignatureTypes = [.. locals],
        };
    }

    private sealed class OperandCalledMethodResolver : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance method ? method.Value : null;
    }

    // Sized declares 16 bytes and has only a static and a literal field (the literal
    // read as not static, so its own test decides); Fielded declares 16 bytes and has
    // an instance field.
    private sealed class Program : ITypeDefinitionResolver, IFieldRepository, ITypeRepository, IMethodRepository
    {
        private static readonly EntityKey StaticField = new(Assembly, 0x04000001);
        private static readonly EntityKey LiteralField = new(Assembly, 0x04000002);
        private static readonly EntityKey InstanceField = new(Assembly, 0x04000003);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => identity.Equals(Sized)
            ? new(SizedKey, "Fixture", "Sized", true, [StaticField, LiteralField], []) { DeclaredSize = 16 }
            : new(FieldedKey, "Fixture", "Fielded", true, [InstanceField], []) { DeclaredSize = 16 };

        public FieldDefinitionModel GetField(EntityKey key) => key == StaticField
            ? new(key, SizedKey, "Shared", CliValueKind.I4, true)
            : key == LiteralField
                ? new(key, SizedKey, "Limit", CliValueKind.I4, false) { IsLiteral = true }
                : new(key, FieldedKey, "Value", CliValueKind.I4, false);

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }
}
