using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// A method that reads a BeforeFieldInit type's static fields on every path
// checks the type once, at its entry: one check covers every field of the type,
// and a body that already checks at its entry stays as it is.
public sealed class BeforeFieldInitEntryInitializerTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly EntityKey OwnerKey = new(Assembly, 0x02000001);
    private static readonly EntityKey InitializerKey = new(Assembly, 0x06000001);
    private static readonly EntityKey FirstKey = new(Assembly, 0x04000001);
    private static readonly EntityKey SecondKey = new(Assembly, 0x04000002);

    [Fact]
    public void ChecksATypeOnceForAllOfItsFields()
    {
        var body = Body(
            I(0, CilOperation.LoadStaticField, new CilOperand.Entity(FirstKey)), I(1, CilOperation.Pop),
            I(2, CilOperation.LoadStaticField, new CilOperand.Entity(SecondKey)), I(3, CilOperation.Pop),
            I(4, CilOperation.Return));

        var rewritten = Initializer().Rewrite(body);

        Assert.Equal(
            [CilOperation.LoadStaticFieldAddress, CilOperation.Pop, CilOperation.LoadStaticField, CilOperation.Pop,
                CilOperation.LoadStaticField, CilOperation.Pop, CilOperation.Return],
            rewritten.Instructions.Select(instruction => instruction.Operation));
        Assert.Equal(new CilOperand.Entity(FirstKey), rewritten.Instructions[0].Operand);
        Assert.Same(rewritten, Initializer().Rewrite(rewritten));
    }

    private static BeforeFieldInitEntryInitializer Initializer()
    {
        var program = new Program();
        return new(new ControlFlowGraphBuilderFactory().Create(), new ControlFlowDominanceAnalyzer(),
            program, program, program, enabled: true);
    }

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    private static CilMethodBody Body(params CilInstruction[] instructions) =>
        new(new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void), 1), 1, [], [.. instructions]);

    private sealed class Program : ITypeRepository, IMethodRepository, IFieldRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) =>
            new(OwnerKey, "Fixture", "Cache", false, [FirstKey, SecondKey], [InitializerKey]) { IsBeforeFieldInit = true };

        public MethodDefinitionModel GetMethod(EntityKey key) =>
            new(InitializerKey, OwnerKey, ".cctor", true, MethodSignatureModel.Create(CliValueKind.Void), 1);

        public FieldDefinitionModel GetField(EntityKey key) =>
            new(key, OwnerKey, key == FirstKey ? "First" : "Second", CliValueKind.I4, true)
            {
                SignatureType = CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            };
    }
}
