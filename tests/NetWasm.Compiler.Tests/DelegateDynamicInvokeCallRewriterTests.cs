using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

// Delegate.DynamicInvoke whose receiver's static type is a sealed delegate type calls
// DelegateMetadata.DynamicInvoke<TDelegate>; any other receiver keeps the call.
public sealed class DelegateDynamicInvokeCallRewriterTests
{
    private static readonly AssemblyIdentity CoreLib = new("System.Private.CoreLib");
    private static readonly EntityKey DelegateType = new(CoreLib, 0x02000001);
    private static readonly EntityKey MetadataType = new(CoreLib, 0x02000002);
    private static readonly EntityKey InvokerKey = new(CoreLib, 0x06000002);
    private static readonly CliTypeIdentity Action = CliTypeIdentity.Named(CoreLib, "System", "Action", false);
    private static readonly CliTypeIdentity Delegate = CliTypeIdentity.Named(CoreLib, "System", "Delegate", false);
    private static readonly CliTypeIdentity Metadata =
        CliTypeIdentity.Named(CoreLib, "System.Runtime.CompilerServices", "DelegateMetadata", false);

    [Fact]
    public void ABodyWithoutDynamicInvokeIsNotAnalyzed()
    {
        var types = new FixedTypes(null);
        var body = Body(CilOperation.Call, CilOperation.Return);

        Assert.Same(body, Rewriter(types, dynamicInvoke: false).Rewrite(body));
        Assert.False(types.Analyzed);
    }

    // The receiver's static type: unknown, an abstract delegate type, or not a type definition.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AReceiverOfNoKnownDelegateTypeKeepsTheCall(int receiver)
    {
        var stack = receiver switch
        {
            0 => ImmutableArray<CliTypeIdentity?>.Empty.Add(null).Add(null),
            1 => [Delegate, null],
            2 => [CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference), null],
            _ => [null],
        };
        var body = Body(CilOperation.CallVirtual, CilOperation.Return);

        Assert.Same(body, Rewriter(new FixedTypes(stack), dynamicInvoke: true).Rewrite(body));
    }

    [Theory]
    [InlineData(CilOperation.Call)]
    [InlineData(CilOperation.CallVirtual)]
    public void ASealedDelegateReceiverCallsTheInvokerForItsType(CilOperation operation)
    {
        var instances = new Instances();
        var body = Body(operation, CilOperation.Return);

        var rewritten = Rewriter(new FixedTypes([Action, null]), dynamicInvoke: true, instances).Rewrite(body);

        var call = rewritten.Instructions[0];
        Assert.Equal(CilOperation.Call, call.Operation);
        Assert.Same(instances.Resolved, Assert.IsType<CilOperand.MethodInstance>(call.Operand).Value);
        Assert.Equal((CoreLib, InvokerKey.MetadataToken), instances.Requested);
        Assert.Equal([Action], instances.Context?.MethodArguments);
        Assert.Equal(body.Instructions[1], rewritten.Instructions[1]);
    }

    private static DelegateDynamicInvokeCallRewriter Rewriter(
        FixedTypes types, bool dynamicInvoke, Instances? instances = null)
    {
        var definitions = new Definitions();
        return new(
            new Calls(dynamicInvoke),
            definitions,
            definitions,
            instances ?? new Instances(),
            new Symbols(),
            new ControlFlowGraphBuilderFactory().Create(),
            new PassThrough(),
            types);
    }

    private static CilMethodBody Body(params CilOperation[] operations) => new(
        new MethodDefinitionModel(new EntityKey(CoreLib, 0x06000009), MetadataType, "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void), 0),
        2,
        [],
        [.. operations.Select((operation, offset) => new CilInstruction(offset, offset + 1, operation, new CilOperand.None()))]);

    private sealed class FixedTypes(ImmutableArray<CliTypeIdentity?>? stack) : IStaticReferenceTypeAnalyzer
    {
        internal bool Analyzed { get; private set; }

        public ImmutableDictionary<int, ImmutableArray<CliTypeIdentity?>> Analyze(ValidatedControlFlowGraph controlFlow)
        {
            Analyzed = true;
            return stack is { } types ? ImmutableDictionary<int, ImmutableArray<CliTypeIdentity?>>.Empty.Add(0, types) : [];
        }
    }

    // Offset 0 calls Delegate.DynamicInvoke(object[]) or, without it, a static method of another name.
    private sealed class Calls(bool dynamicInvoke) : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction)
        {
            if (instruction.Offset != 0) return null;
            var signature = MethodSignatureModel.Create(CliValueKind.ManagedReference, CliValueKind.ManagedReference);
            var definition = new MethodDefinitionModel(new EntityKey(CoreLib, 0x06000001), DelegateType,
                dynamicInvoke ? "DynamicInvoke" : "Invoke", IsStatic: false, signature, 0);
            return new(definition, Delegate, [], signature);
        }
    }

    private sealed class Definitions : ITypeDefinitionResolver, IMethodRepository
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(identity.Equals(Metadata) ? MetadataType : DelegateType, "System", identity.CanonicalName, false,
                [], identity.Equals(Metadata) ? [new EntityKey(CoreLib, 0x06000003), InvokerKey] : [])
            {
                IsSealed = identity.Equals(Action),
            };

        public MethodDefinitionModel GetMethod(EntityKey key) => new(key, MetadataType,
            key == InvokerKey ? "DynamicInvoke" : "GetInvokeMethod", true,
            MethodSignatureModel.Create(CliValueKind.ManagedReference), 0);
    }

    private sealed class Instances : IMethodInstanceResolver
    {
        internal MethodInstanceModel Resolved { get; } = new(
            new MethodDefinitionModel(InvokerKey, MetadataType, "DynamicInvoke", true,
                MethodSignatureModel.Create(CliValueKind.ManagedReference), 0),
            Metadata,
            [Action],
            MethodSignatureModel.Create(CliValueKind.ManagedReference));

        internal (AssemblyIdentity, int)? Requested { get; private set; }

        internal CliGenericContext? Context { get; private set; }

        public MethodInstanceModel ResolveMethodInstance(
            AssemblyIdentity source, int metadataToken, string methodDisplayName, int ilOffset,
            CliGenericContext? genericContext = null)
        {
            Requested = (source, metadataToken);
            Context = genericContext;
            return Resolved;
        }
    }

    private sealed class Symbols : ISymbolFormatter
    {
        public string Format(EntityKey key) => key == DelegateType ? "System.Delegate" : "System.Runtime.CompilerServices.DelegateMetadata";

        public string Format(MethodDefinitionModel method) => method.Name;
    }

    private sealed class PassThrough : ITypedStackValidator
    {
        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph) => new(graph,
            graph.Blocks.ToImmutableDictionary(block => block.Index, _ => ImmutableArray<CliValueKind>.Empty),
            graph.Blocks.SelectMany(block => block.Instructions)
                .ToImmutableDictionary(instruction => instruction.Offset, _ => ImmutableArray<CliValueKind>.Empty));
    }
}
