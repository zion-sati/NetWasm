using System.Diagnostics.CodeAnalysis;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Analysis;

// Expression.New(typeof(C)) becomes Expression.NewParameterless(C's constructor, C) for a concrete class with a
// parameterless constructor; any other class keeps New(Type), and a CoreLib without NewParameterless is refused.
public sealed class ParameterlessNewExpressionRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("ExpressionFixture");
    private static readonly EntityKey Created = new(Assembly, 0x02000001);
    private static readonly EntityKey Constructor = new(Assembly, 0x06000001);
    private static readonly EntityKey Helper = new(Assembly, 0x06000002);
    private static readonly EntityKey TypeType = new(Assembly, 0x02000002);
    private static readonly EntityKey ExpressionType = new(Assembly, 0x02000003);

    [Theory]
    [InlineData("folds")]
    [InlineData("abstract")]
    [InlineData("no parameterless constructor")]
    public void OnlyAConcreteClassWithAParameterlessConstructorFolds(string scenario)
    {
        var fixture = new Fixture(scenario);
        var body = Fixture.Body();

        var rewritten = fixture.Rewriter().Rewrite(body);

        if (scenario != "folds")
        {
            Assert.Same(body, rewritten);
            return;
        }
        Assert.Equal([CilOperation.LoadMethodToken, CilOperation.LoadTypeToken, CilOperation.Call, CilOperation.Return],
            rewritten.Instructions.Select(instruction => instruction.Operation));
        Assert.Equal("NewParameterless",
            Assert.IsType<CilOperand.MethodInstance>(rewritten.Instructions[2].Operand).Value.Definition.Name);
        Assert.Equal(body.MaxStack + 1, rewritten.MaxStack);
    }

    [Fact]
    public void ACoreLibWithoutTheHelperIsRefused()
    {
        var fixture = new Fixture("no helper");
        var error = Assert.Throws<CompilerException>(() => fixture.Rewriter().Rewrite(Fixture.Body()));
        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
    }

    private sealed class Fixture(string scenario) :
        ICalledMethodResolver, ITypeDefinitionResolver, IMethodRepository, IMethodInstanceResolver, ISymbolFormatter, ITypeFinder
    {
        private static readonly CliTypeIdentity CreatedType = CliTypeIdentity.Named(Assembly, "Fixture", "Created", false);
        private static readonly CliTypeIdentity SystemType = CliTypeIdentity.Named(Assembly, "System", "Type", false);

        internal static CilMethodBody Body() => new(Definition(0x06000010, ExpressionType, "Build", MethodSignatureModel.Create(CliValueKind.Void)), 1, [],
        [
            new(0, 1, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(CreatedType)),
            new(1, 2, CilOperation.Call, Call(Definition(0x06000011, TypeType, "GetTypeFromHandle",
                new MethodSignatureModel(SystemType, [CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference)])))),
            new(2, 3, CilOperation.Call, Call(Definition(0x06000012, ExpressionType, "New",
                new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference), [SystemType])))),
            new(3, 4, CilOperation.Return, new CilOperand.None()),
        ]);

        internal ParameterlessNewExpressionRewriter Rewriter() => new(this, this, this, this, this, this);

        private static CilOperand.MethodInstance Call(MethodDefinitionModel definition) =>
            new(new(definition, CliTypeIdentity.Named(Assembly, "Fixture", "Owner", false), [],
                definition.Signature));

        private static MethodDefinitionModel Definition(int token, EntityKey owner, string name, MethodSignatureModel signature) =>
            new(new(Assembly, token), owner, name, name != ".ctor", signature, 0);

        public MethodInstanceModel? Resolve(CilInstruction instruction) => (instruction.Operand as CilOperand.MethodInstance)?.Value;

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(Created, "Fixture", "Created", false, [], [Constructor]) { IsAbstract = scenario == "abstract" };

        public MethodDefinitionModel GetMethod(EntityKey key) => key == Helper
            ? Definition(Helper.MetadataToken, ExpressionType, "NewParameterless", MethodSignatureModel.Create(CliValueKind.ManagedReference))
            : Definition(Constructor.MetadataToken, Created, ".ctor", scenario == "no parameterless constructor"
                ? MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4)
                : MethodSignatureModel.Create(CliValueKind.Void));

        public MethodInstanceModel ResolveMethodInstance(AssemblyIdentity source, int metadataToken, string methodDisplayName,
            int ilOffset, CliGenericContext? genericContext = null)
        {
            var definition = GetMethod(new(source, metadataToken));
            return new(definition, CreatedType, [], definition.Signature);
        }

        public string Format(EntityKey key) =>
            key == TypeType ? "System.Type" : key == ExpressionType ? "System.Linq.Expressions.Expression" : "Fixture.Created";

        public string Format(MethodDefinitionModel method) => method.Name;

        public TypeDefinitionModel FindType(string fullName) =>
            new(ExpressionType, "System.Linq.Expressions", "Expression", false, [], scenario == "no helper" ? [] : [Helper]);
    }
}
