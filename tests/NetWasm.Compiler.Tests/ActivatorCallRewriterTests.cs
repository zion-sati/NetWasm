using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class ActivatorCallRewriterTests
{
    [Theory]
    [InlineData("public", true, CilOperation.NewObject)]
    [InlineData("private", false, CilOperation.Call)]
    [InlineData("missing", false, CilOperation.Call)]
    [InlineData("abstract", false, CilOperation.Call)]
    [InlineData("interface", false, CilOperation.Call)]
    [InlineData("value", true, CilOperation.DefaultValue)]
    [InlineData("array", false, CilOperation.Call)]
    [InlineData("vector", false, CilOperation.Call)]
    public void RewritePreservesClosedConstructorEligibility(
        string scenario, bool eligible, CilOperation construction)
    {
        var fixture = new Fixture(scenario);
        var rewriter = new ActivatorCallRewriter(
            fixture, fixture, fixture, fixture);
        var eligibility = rewriter.Rewrite(fixture.Body("HasDefaultConstructor"));
        var check = Assert.Single(eligibility.Instructions);
        Assert.Equal(CilOperation.LoadInt32, check.Operation);
        Assert.Equal(eligible ? 1 : 0, Assert.IsType<CilOperand.ConstantI4>(check.Operand).Value);

        var original = fixture.Body("CreateDefaultInstance");
        var result = rewriter.Rewrite(original);
        var instruction = Assert.Single(result.Instructions);
        Assert.Equal(construction, instruction.Operation);
        if (construction == CilOperation.Call)
            Assert.Same(original, result);
        else if (construction == CilOperation.DefaultValue)
            Assert.Equal(fixture.Identity, Assert.IsType<CilOperand.TypeIdentity>(instruction.Operand).Value);
        else
            Assert.Equal(fixture.Constructor.Key,
                Assert.IsType<CilOperand.MethodInstance>(instruction.Operand).Value.Definition.Key);
    }

    [Fact]
    public void RewriteLeavesThePublicWrapperAndItsMethodAddressIntact()
    {
        var fixture = new Fixture("public");
        var rewriter = new ActivatorCallRewriter(fixture, fixture, fixture, fixture);
        var call = fixture.Body("CreateInstance");
        var address = call with
        {
            Instructions = [call.Instructions[0] with { Operation = CilOperation.LoadFunction }],
        };
        Assert.Same(call, rewriter.Rewrite(call));
        Assert.Same(address, rewriter.Rewrite(address));
    }

    [Fact]
    public void RewriteRejectsAnUnboundGenericArgument()
    {
        var fixture = new Fixture("public");
        var rewriter = new ActivatorCallRewriter(fixture, fixture, fixture, fixture);
        var body = fixture.Body("CreateDefaultInstance");
        var call = body.Instructions[0];
        var method = Assert.IsType<CilOperand.MethodInstance>(call.Operand).Value;
        body = body with
        {
            Instructions = [call with
        {
            Operand = new CilOperand.MethodInstance(method with
            {
                MethodArguments = [CliTypeIdentity.GenericParameter(true, 0)],
            }),
        }]
        };
        Assert.Throws<CompilerException>(() => rewriter.Rewrite(body));
    }

    private sealed class Fixture : ITypeDefinitionResolver, IMethodRepository,
        IMethodInstanceResolver, ISymbolFormatter
    {
        private static readonly AssemblyIdentity Assembly = new("Activation.Tests");
        private readonly TypeDefinitionModel _type;
        public CliTypeIdentity Identity { get; }
        public MethodDefinitionModel Constructor { get; }

        public Fixture(string scenario)
        {
            var key = new EntityKey(Assembly, 0x02000001);
            Constructor = new(new(Assembly, 0x06000001), key, ".ctor", false,
                MethodSignatureModel.Create(CliValueKind.Void), 1)
            {
                IsPublic = scenario != "private",
            };
            _type = new(key, "Tests", "Value", scenario == "value", [],
                scenario is "missing" or "value" or "interface" ? [] : [Constructor.Key])
            {
                IsAbstract = scenario == "abstract",
                IsInterface = scenario == "interface",
            };
            var identity = CliTypeIdentity.FromDefinition(_type);
            Identity = scenario switch
            {
                "array" => CliTypeIdentity.Array(identity, 2),
                "vector" => CliTypeIdentity.SzArray(identity),
                _ => identity,
            };
        }

        public CilMethodBody Body(string name)
        {
            var definition = Constructor with { Name = name, IsStatic = true };
            var method = new MethodInstanceModel(definition,
                CliTypeIdentity.Named(Assembly, "System", "Activator", false), [Identity], definition.Signature);
            return new(definition, 1, [],
                [new CilInstruction(0, 1, CilOperation.Call, new CilOperand.MethodInstance(method))]);
        }

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => _type;
        public MethodDefinitionModel GetMethod(EntityKey key) => Constructor;
        public MethodInstanceModel ResolveMethodInstance(AssemblyIdentity source, int metadataToken,
            string methodDisplayName, int ilOffset, CliGenericContext? genericContext = null) =>
            new(Constructor, Identity, [], Constructor.Signature);
        public string Format(EntityKey key) => "System.Activator";
        public string Format(MethodDefinitionModel method) => method.Name;
    }
}
