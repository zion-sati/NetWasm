using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Tests;

// Counted loops whose body writes the same blocks on every iteration run it
// once: initblk, cpblk and NativeMemory's Fill and Copy into locals, through
// conversions, constant offsets and temporaries. A body that reads what an
// iteration changes, takes an address for anything else, or whose addresses
// come from an argument, a sum of two addresses or values carried into the
// loop keeps its loop.
public sealed class IdempotentLoopCollapserTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly EntityKey Owner = new(Assembly, 0x02000001);
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity NativeInt = CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt);
    private static readonly CliTypeIdentity Block = CliTypeIdentity.Named(Assembly, "Fixture", "Block", isValueType: true);
    private static readonly CliTypeIdentity Object = CliTypeIdentity.Named(Assembly, "System", "Object", isValueType: false);

    // The counter, two blocks, a native int temporary, two ints and an object.
    private static readonly ImmutableArray<CliTypeIdentity> Locals = [Int32, Block, Block, NativeInt, Int32, Int32, Object];

    // NativeMemory.Fill(void*, nuint, byte), NativeMemory.Copy(void*, void*, nuint) and nint Offset(int).
    private static readonly MethodDefinitionModel Fill = Method(0x10, "Fill", CliValueKind.Void,
        CliValueKind.NativeInt, CliValueKind.NativeInt, CliValueKind.I4);
    private static readonly MethodDefinitionModel Copy = Method(0x11, "Copy", CliValueKind.Void,
        CliValueKind.NativeInt, CliValueKind.NativeInt, CliValueKind.NativeInt);
    private static readonly MethodDefinitionModel Offset = Method(0x12, "Offset", CliValueKind.NativeInt, CliValueKind.I4);

    public static TheoryData<string> CollapsedShapes() =>
    [
        "initblk of a local", "cpblk between locals", "address behind conversions and a constant offset",
        "constant offset on the left", "offset behind a numeric conversion", "64-bit offset", "address through a temporary",
        "temporary written before it is read", "NativeMemory.Fill of a local",
        "NativeMemory.Copy between locals",
    ];

    [Theory]
    [MemberData(nameof(CollapsedShapes))]
    public void ShapeRunsItsBodyOnce(string shape)
    {
        var body = Shape(shape);

        var rewritten = Collapser().Rewrite(body);

        Assert.NotSame(body, rewritten);
        Assert.DoesNotContain(rewritten.Instructions, static instruction =>
            instruction.Operation is CilOperation.Branch or CilOperation.BranchIfLessThanSigned);
        Validator().Validate(new ControlFlowGraphBuilderFactory().Create().Build(rewritten));
    }

    public static TheoryData<string> LoopShapes() =>
    [
        "local read before the iteration writes it", "copy from an argument", "address taken for something else",
        "block local also stored", "block read as a value", "block of a reference local", "body without local types",
        "copy onto itself", "sum of two addresses", "temporary stored before the loop", "destination carried into the loop",
        "converted address carried into the loop", "offset of a duplicated value", "offset from a call",
        "temporary carried into the loop", "offset behind a conversion of a carried value", "offset carried into the loop",
    ];

    [Theory]
    [MemberData(nameof(LoopShapes))]
    public void ShapeKeepsItsLoop(string shape)
    {
        var body = Shape(shape);

        Assert.Same(body, Collapser().Rewrite(body));
    }

    private static CilMethodBody Shape(string shape) => shape switch
    {
        "initblk of a local" => Body(code => code.Loop(0, inner => inner.Clear(1))),
        "cpblk between locals" => Body(code => code.Loop(0, inner => inner
            .Local(CilOperation.LoadLocalAddress, 1).Local(CilOperation.LoadLocalAddress, 2).Op(CilOperation.LoadInt32, 16)
            .Op(CilOperation.CopyBlock))),
        "address behind conversions and a constant offset" => Body(code => code.Loop(0, inner => inner
            .Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.ConvertNativeUInt)
            .Op(CilOperation.LoadInt32, 4).Op(CilOperation.ConvertNativeInt).Op(CilOperation.Add).Fill())),
        "constant offset on the left" => Body(code => code.Loop(0, inner => inner
            .Op(CilOperation.LoadInt32, 4).Op(CilOperation.ConvertNativeInt).Local(CilOperation.LoadLocalAddress, 1)
            .Op(CilOperation.Add).Fill())),
        "offset behind a numeric conversion" => Body(code => code.Loop(0, inner => inner
            .Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.LoadInt32, 4)
            .Op(CilOperation.ConvertNumeric, new CilOperand.NumericConversion(64, true, false, false, false))
            .Op(CilOperation.ConvertNativeUInt).Op(CilOperation.Add).Fill())),
        "64-bit offset" => Body(code => code.Loop(0, inner => inner
            .Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.LoadInt64, 4).Op(CilOperation.ConvertNativeUInt)
            .Op(CilOperation.Add).Fill())),
        "address through a temporary" => Body(code => code.Loop(0, inner => inner
            .Local(CilOperation.LoadLocalAddress, 1).Local(CilOperation.StoreLocal, 3).Local(CilOperation.LoadLocal, 3).Fill())),
        "temporary written before it is read" => Body(code => code.Loop(0, inner => inner
            .Op(CilOperation.LoadInt32, 5).Local(CilOperation.StoreLocal, 4).Local(CilOperation.LoadLocal, 4).Op(CilOperation.Pop)
            .Clear(1))),
        "NativeMemory.Fill of a local" => Body(code => code.Loop(0, inner => inner
            .Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.LoadInt32, 16).Op(CilOperation.ConvertNativeUInt)
            .Op(CilOperation.LoadInt32, 0).Op(CilOperation.Call, new CilOperand.Entity(Fill.Key)))),
        "NativeMemory.Copy between locals" => Body(code => code.Loop(0, inner => inner
            .Local(CilOperation.LoadLocalAddress, 2).Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.LoadInt32, 16)
            .Op(CilOperation.ConvertNativeUInt).Op(CilOperation.Call, new CilOperand.Entity(Copy.Key)))),
        "local read before the iteration writes it" => Body(code => code.Loop(0, inner => inner
            .Local(CilOperation.LoadLocal, 4).Op(CilOperation.Pop).Op(CilOperation.LoadInt32, 5).Local(CilOperation.StoreLocal, 4)
            .Clear(1))),
        "copy from an argument" => Body(code => code.Loop(0, inner => inner
            .Local(CilOperation.LoadLocalAddress, 1).Local(CilOperation.LoadArgument, 0).Op(CilOperation.LoadInt32, 16)
            .Op(CilOperation.CopyBlock))),
        "address taken for something else" => Body(code => code.Loop(0, inner => inner
            .Local(CilOperation.LoadLocalAddress, 2).Op(CilOperation.Pop).Clear(1))),
        "block local also stored" => Body(code => code.Loop(0, inner => inner
            .Clear(5).Op(CilOperation.LoadInt32, 7).Local(CilOperation.StoreLocal, 5))),
        "block read as a value" => Body(code => code.Loop(0, inner => inner
            .Clear(1).Local(CilOperation.LoadLocal, 1).Op(CilOperation.Pop))),
        "block of a reference local" => Body(code => code.Loop(0, inner => inner.Clear(6))),
        "body without local types" => Body(code => code.Loop(0, inner => inner.Clear(1))) with { LocalSignatureTypes = [] },
        "copy onto itself" => Body(code => code.Loop(0, inner => inner
            .Local(CilOperation.LoadLocalAddress, 1).Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.LoadInt32, 16)
            .Op(CilOperation.CopyBlock))),
        "sum of two addresses" => Body(code => code.Loop(0, inner => inner
            .Local(CilOperation.LoadLocalAddress, 1).Local(CilOperation.LoadArgument, 0).Op(CilOperation.Add).Fill())),
        "temporary stored before the loop" => Body(code => code
            .Local(CilOperation.LoadLocalAddress, 1).Local(CilOperation.StoreLocal, 3)
            .Loop(0, inner => inner.Local(CilOperation.LoadLocal, 3).Fill())),
        // Each iteration leaves the address the next one writes through.
        "destination carried into the loop" => Body(code => code
            .Local(CilOperation.LoadLocalAddress, 1)
            .Loop(0, inner => inner.Fill().Local(CilOperation.LoadLocalAddress, 1))
            .Op(CilOperation.Pop)),
        "converted address carried into the loop" => Body(code => code
            .Local(CilOperation.LoadLocalAddress, 1)
            .Loop(0, inner => inner.Op(CilOperation.ConvertNativeUInt).Fill().Local(CilOperation.LoadLocalAddress, 1))
            .Op(CilOperation.Pop)),
        "offset of a duplicated value" => Body(code => code.Loop(0, inner => inner
            .Op(CilOperation.LoadInt32, 4).Op(CilOperation.ConvertNativeInt).Op(CilOperation.Duplicate).Op(CilOperation.Add).Fill())),
        "offset from a call" => Body(code => code.Loop(0, inner => inner
            .Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.LoadInt32, 4).Op(CilOperation.Call, new CilOperand.Entity(Offset.Key))
            .Op(CilOperation.Add).Fill())),
        "temporary carried into the loop" => Body(code => code
            .Local(CilOperation.LoadLocalAddress, 1)
            .Loop(0, inner => inner.Local(CilOperation.StoreLocal, 3).Local(CilOperation.LoadLocal, 3).Fill()
                .Local(CilOperation.LoadLocalAddress, 1))
            .Op(CilOperation.Pop)),
        // Each iteration adds the offset the previous one left.
        "offset carried into the loop" => Body(code => code
            .Op(CilOperation.LoadInt32, 4).Op(CilOperation.ConvertNativeInt)
            .Loop(0, inner => inner.Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.Add)
                .Fill().Op(CilOperation.LoadInt32, 4).Op(CilOperation.ConvertNativeInt))
            .Op(CilOperation.Pop)),
        _ => Body(code => code
            .Op(CilOperation.LoadInt32, 4).Op(CilOperation.ConvertNativeInt)
            .Loop(0, inner => inner.Op(CilOperation.ConvertNativeInt).Local(CilOperation.LoadLocalAddress, 1).Op(CilOperation.Add)
                .Fill().Op(CilOperation.LoadInt32, 4).Op(CilOperation.ConvertNativeInt))
            .Op(CilOperation.Pop)),
    };

    private static IdempotentLoopCollapser Collapser() =>
        new(new ControlFlowGraphBuilderFactory().Create(), Validator(), new Program(), enabled: true);

    private static ITypedStackValidator Validator()
    {
        var program = new Program();
        return new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program);
    }

    // static void Run(nint pointer): the code, then return.
    private static CilMethodBody Body(Func<CilBuilder, CilBuilder> write)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.NativeInt);
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), Owner, "Run", true, signature, 1);
        return new(method, 8, [.. Locals.Select(local => local.StackKind)], write(new CilBuilder()).Return().Build())
        {
            LocalSignatureTypes = Locals,
        };
    }

    private static MethodDefinitionModel Method(int token, string name, CliValueKind result, params CliValueKind[] parameters) =>
        new(new(Assembly, 0x06000000 + token), Owner, name, true, MethodSignatureModel.Create(result, parameters), 1);

    private sealed class Program : ITypeRepository, IFieldRepository, IMethodRepository, IRuntimeIntrinsicRegistry
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => new(key, "Fixture", "Owner", false, [], []);

        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => new[] { Fill, Copy, Offset }.Single(method => method.Key == key);

        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = method == Fill.Key ? RuntimeIntrinsic.NativeMemoryFill : RuntimeIntrinsic.NativeMemoryCopy;
            return method == Fill.Key || method == Copy.Key;
        }
    }
}

// The block writes the collapser tests compose.
internal static class BlockCil
{
    extension(CilBuilder code)
    {
        // initblk(address on the stack, 0, 8).
        public CilBuilder Fill() => code.Op(CilOperation.LoadInt32, 0).Op(CilOperation.LoadInt32, 8).Op(CilOperation.InitializeBlock);

        // initblk(&local, 0, 4).
        public CilBuilder Clear(int local) => code.Local(CilOperation.LoadLocalAddress, local).Op(CilOperation.LoadInt32, 0)
            .Op(CilOperation.LoadInt32, 4).Op(CilOperation.InitializeBlock);
    }
}
