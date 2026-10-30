using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

internal static class ConstantRewriteTestSupport
{
    internal static readonly AssemblyIdentity Assembly = new("ConstantsFixture");
    internal static readonly CliTypeIdentity Owner = CliTypeIdentity.Named(Assembly, "Fixture", "Helpers", false);
    internal static readonly EntityKey TypeKey = new(Assembly, 0x02000001);

    internal static MethodInstanceModel Method(int token = 1) => new(
        new(new(Assembly, 0x06000000 + token), TypeKey, "UserPredicate", true,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.ManagedReference, CliValueKind.ManagedReference), 1),
        Owner, [], MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.ManagedReference, CliValueKind.ManagedReference));

    internal static CilInstruction Instruction(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    internal static CilMethodBody Body(params CilInstruction[] instructions) => new(Method().Definition, 4, [CliValueKind.I4], [.. instructions]);

    internal static ConstantOperandReader Operands(MethodFacts facts) => new(facts, facts, facts, facts);

    internal static ConstantComparisonEvaluator Comparisons(MethodFacts facts) => new(facts);

    internal static ConstantCallEvaluator Evaluator(MethodFacts facts) =>
        new(facts, facts, facts, facts, facts, Operands(facts), Comparisons(facts), new NetWasm.Compiler.Analysis.MethodSpecializer([]));

    internal static IControlFlowGraphBuilder Graph(CilMethodBody body) => new FixedGraph(
        new ControlFlowGraphBuilderFactory().Create().Build(body));

    private sealed class FixedGraph(ControlFlowGraph graph) : IControlFlowGraphBuilder
    {
        public ControlFlowGraph Build(CilMethodBody body)
        {
            Assert.Same(graph.MethodBody, body);
            return graph;
        }
    }

    internal sealed class MethodFacts : IMethodBodyReader, ICalledMethodResolver, ITypeDefinitionResolver, IMethodRepository, IRuntimeIntrinsicRegistry,
        IFieldRepository, ITypeRepository
    {
        internal Dictionary<EntityKey, CilMethodBody> Bodies { get; } = [];
        internal Dictionary<EntityKey, FieldDefinitionModel> Fields { get; } = [];
        internal bool HasInitializer { get; set; }
        internal bool IsIntrinsic { get; set; }
        internal TypeDefinitionModel Definition { get; set; } = new(TypeKey, "Fixture", "Helpers", false, [], []);

        public CilMethodBody ReadMethodBody(MethodDefinitionModel method) => Bodies[method.Key];
        public CilMethodBody ReadMethodBody(MethodInstanceModel method) => Bodies[method.Definition.Key];
        public MethodInstanceModel? Resolve(CilInstruction instruction) => (instruction.Operand as CilOperand.MethodInstance)?.Value;
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => HasInitializer
            ? Definition with { Methods = [new EntityKey(Assembly, 0x0600ffff)] }
            : Definition;
        public FieldDefinitionModel GetField(EntityKey key) => Fields[key];
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => ResolveTypeIdentity(Owner);
        public MethodDefinitionModel GetMethod(EntityKey key) => Method().Definition with { Key = key, Name = ".cctor" };
        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = default;
            return IsIntrinsic;
        }

        internal IMethodRewriteRule ReturnEpilogues { get; set; } = new UnchangedBody();
        internal IPureReferenceComparisonClassifier Classifier() => new PureReferenceComparisonClassifier(this, this, this, this, ReturnEpilogues, this);
    }

    private sealed class UnchangedBody : IMethodRewriteRule
    {
        public CilMethodBody Rewrite(CilMethodBody body) => body;
    }

    internal sealed class Comparison(ReferenceComparison? kind) : IPureReferenceComparisonClassifier
    {
        public ReferenceComparison? Classify(MethodInstanceModel method) => kind;
    }

    internal sealed class TypeComparison(bool? equal) : IClosedTypeIdentityComparer
    {
        public bool? Compare(CliTypeIdentity left, CliTypeIdentity right) => equal;
    }
}
