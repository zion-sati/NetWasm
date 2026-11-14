using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Analysis.StaticArrays;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Analysis.StaticArrays;

internal static class StaticArrayTestSupport
{
    internal static readonly AssemblyIdentity Assembly = new("StaticArrays");
    internal static readonly EntityKey HolderKey = new(Assembly, 0x02000001);
    internal static readonly EntityKey ElementKey = new(Assembly, 0x02000002);
    internal static readonly CliTypeIdentity Holder = CliTypeIdentity.Named(Assembly, "Fixture", "Holder", false);
    internal static readonly CliTypeIdentity Element = CliTypeIdentity.Named(Assembly, "Fixture", "Element", true);
    internal static readonly FieldDefinitionModel Table = new(new(Assembly, 0x04000001), HolderKey, "Table", CliTypeIdentity.SzArray(Element), true) { IsInitOnly = true };
    internal static readonly FieldDefinitionModel Value = new(new(Assembly, 0x04000002), ElementKey, "Value", CliValueKind.I8, false);

    internal static MethodInstanceModel Constructor()
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I8);
        return new(new(new(Assembly, 0x06000002), ElementKey, ".ctor", false, signature, 1), Element, [], signature);
    }

    internal static ManagedMethodBody Initializer(int length = 1, int index = 0)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        var method = new MethodInstanceModel(new(new(Assembly, 0x06000001), HolderKey, ".cctor", true, signature, 1), Holder, [], signature);
        return Managed(new(method.Definition, 4, [],
        [
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(length)),
            I(1, CilOperation.NewArray, new CilOperand.TypeIdentity(Element)),
            I(2, CilOperation.Duplicate),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(index)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(42)),
            I(5, CilOperation.ConvertInt64),
            I(6, CilOperation.NewObject, new CilOperand.MethodInstance(Constructor())),
            I(7, CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Element)),
            I(8, CilOperation.StoreStaticField, new CilOperand.Entity(Table.Key)),
            I(9, CilOperation.Return),
        ])
        { MethodInstance = method });
    }

    internal static ManagedMethodBody Managed(CilMethodBody body) => new(body.MethodInstance!,
        new(new ControlFlowGraphBuilderFactory().Create().Build(body), ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));

    internal static ReachableProgram Program(ManagedMethodBody body) => new(null,
        ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(body.Method.Definition.Key, body), [], [], [], [], [], [],
        ImmutableDictionary<EntityKey, MethodRootMap>.Empty, ImmutableDictionary<CliTypeIdentity, MethodInstanceModel>.Empty, []);

    internal static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) => new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    internal sealed class Facts : IMethodBodyReader, ICalleeMethodBodyReader, IFieldRepository, ITypeDefinitionResolver, IMethodRepository,
        ICalledMethodResolver, ITypeOperandResolver, IClosedTypeIdentityComparer, IRuntimeIntrinsicRegistry
    {
        public bool TryReadMethodBody(
            MethodDefinitionModel method,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CilMethodBody? body) =>
            new CalleeMethodBodyReader(this).TryReadMethodBody(method, out body);

        public bool TryReadMethodBody(
            MethodInstanceModel method,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CilMethodBody? body) =>
            new CalleeMethodBodyReader(this).TryReadMethodBody(method, out body);

        internal Dictionary<EntityKey, FieldDefinitionModel> Fields { get; } = new() { [Table.Key] = Table, [Value.Key] = Value };
        internal bool HasInitializer { get; set; }
        internal bool BeforeFieldInit { get; set; }
        internal bool IsIntrinsic { get; set; }
        internal CilMethodBody ConstructorBody { get; set; } = new(Constructor().Definition, 2, [],
        [
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.LoadArgument, new CilOperand.Index(1)),
            I(2, CilOperation.StoreField, new CilOperand.Entity(Value.Key)),
            I(3, CilOperation.Return),
        ]);
        public CilMethodBody ReadMethodBody(MethodDefinitionModel method) => ConstructorBody;
        public CilMethodBody ReadMethodBody(MethodInstanceModel method) => ConstructorBody;
        public FieldDefinitionModel GetField(EntityKey key) => Fields[key];
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => new(ElementKey, "Fixture", "Element", true, [Value.Key],
            HasInitializer ? [new(Assembly, 0x06000003)] : [])
        { IsBeforeFieldInit = BeforeFieldInit };
        public MethodDefinitionModel GetMethod(EntityKey key) => Constructor().Definition with { Key = key, Name = ".cctor" };
        public MethodInstanceModel? Resolve(CilInstruction instruction) => (instruction.Operand as CilOperand.MethodInstance)?.Value;
        public CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? method) => ((CilOperand.TypeIdentity)instruction.Operand).Value;
        public bool? Compare(CliTypeIdentity left, CliTypeIdentity right) => left.Equals(right);
        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = default;
            return IsIntrinsic;
        }
        internal IValueConstructorPurityClassifier Purity() => new ValueConstructorPurityClassifier(this, this, this, this, this);
        internal IStaticArrayPopulationPlanner Planner() => new StaticArrayPopulationPlanner(this, this, this, Purity(), this, this);
    }
}
