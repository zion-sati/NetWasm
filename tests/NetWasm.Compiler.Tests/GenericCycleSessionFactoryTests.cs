using System.Diagnostics.CodeAnalysis;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

// The method instances here model one method definition on Box<T> reached through itself with a larger T each time,
// as PLINQ's CreateOrderedEnumerable is (SortQueryOperator<T, Pair<TKey, TKey2>>).
public sealed class GenericCycleSessionFactoryTests
{
    private static readonly AssemblyIdentity Assembly = new("Fixtures");
    private static readonly EntityKey ExceptionType = new(Assembly, 0x02000100);
    private static readonly EntityKey ExceptionConstructor = new(Assembly, 0x06000100);
    private static readonly CliTypeIdentity Int = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity Long = CliTypeIdentity.FromStackKind(CliValueKind.I8);
    private static readonly CliTypeIdentity BoxDefinition = CliTypeIdentity.Named(Assembly, "Fixtures", "Box`1", false);
    private static readonly MethodSignatureModel Signature = MethodSignatureModel.Create(CliValueKind.Void);
    private static readonly MethodDefinitionModel Grow = Definition(0x06000001, "Grow");
    private static readonly MethodDefinitionModel Other = Definition(0x06000002, "Other");

    [Fact]
    public void GrowthAlongAChainIsCutAtTheLimit()
    {
        var session = Cutoff(limit: 2).Create();

        Assert.Null(session.Admit(Instance(Grow, 0), []));
        Assert.Null(session.Admit(Instance(Grow, 1), [Name(Grow, 0)]));
        var body = session.Admit(Instance(Grow, 2), [Name(Grow, 1)]);

        Assert.NotNull(body);
        Assert.Equal(Name(Grow, 2), body.MethodInstance!.CanonicalName);
        Assert.Equal([CilOperation.LoadString, CilOperation.NewObject, CilOperation.Throw],
            body.Instructions.Select(instruction => instruction.Operation));
        Assert.Contains(Name(Grow, 2), ((CilOperand.UserString)body.Instructions[0].Operand).Value);
        Assert.Equal(ExceptionConstructor, ((CilOperand.MethodInstance)body.Instructions[1].Operand).Value.Definition.Key);
        Assert.Equal(1, body.MaxStack);
    }

    [Fact]
    public void GrowthCountsThroughOtherMethodsOnTheChain()
    {
        var session = Cutoff(limit: 1).Create();

        session.Admit(Instance(Grow, 0), []);
        session.Admit(Instance(Other, 1), [Name(Grow, 0)]);

        Assert.NotNull(session.Admit(Instance(Grow, 1), [Name(Other, 1)]));
    }

    [Fact]
    public void AChainThatDoesNotGrowIsNeverCut()
    {
        var session = Cutoff(limit: 1).Create();

        session.Admit(Instance(Grow, 2), []);
        // The same size, then a smaller one: neither is growth.
        Assert.Null(session.Admit(Instance(Other, 2), [Name(Grow, 2)]));
        Assert.Null(session.Admit(Instance(Grow, 1), [Name(Other, 2)]));
        Assert.Null(session.Admit(Instance(Grow, 1, extra: true), [Name(Grow, 1)]));
    }

    [Fact]
    public void ALargerInstantiationNotBuiltFromTheEarlierOneIsNotGrowth()
    {
        // An async ValueTask<int> method awaiting an async ValueTask<T[]> one reaches the builder's Start again with a
        // larger instantiation; it is not built from the earlier one, so it is not growth.
        var session = Cutoff(limit: 1, breadth: 1).Create();
        session.Admit(Instance(Grow, 0), []);

        Assert.Null(session.Admit(new(Grow, Box(Box(Long)), [], Signature), [Name(Grow, 0)]));
        Assert.Null(session.Admit(new(Grow, Box(CliTypeIdentity.SzArray(Long)), [], Signature), [Name(Grow, 0)]));
        // Wrapping the earlier argument is.
        Assert.NotNull(session.Admit(new(Grow, Box(CliTypeIdentity.SzArray(Box(Int))), [], Signature), [Name(Grow, 0)]));
    }

    [Fact]
    public void ALargerInstantiationBuiltFromPartsOfTheEarlierOneIsGrowth()
    {
        // As PLINQ's Join builds ConcatKey<Pair<A, B>, C> from ConcatKey<A, B>: the earlier argument is not contained,
        // but its generic part is.
        var session = Cutoff(limit: 1).Create();
        var earlier = new MethodInstanceModel(Grow, Box(Pair(Box(Int), Long)), [], Signature);
        session.Admit(earlier, []);

        Assert.NotNull(session.Admit(new(Grow, Box(Pair(Pair(Box(Int), Int), Long)), [], Signature), [earlier.CanonicalName]));
        Assert.Null(session.Admit(new(Grow, Box(Pair(Pair(Box(Long), Int), Long)), [], Signature), [earlier.CanonicalName]));
    }

    private static readonly CliTypeIdentity PairDefinition = CliTypeIdentity.Named(Assembly, "Fixtures", "Pair`2", false);

    private static CliTypeIdentity Pair(CliTypeIdentity first, CliTypeIdentity second) =>
        CliTypeIdentity.GenericInstantiation(PairDefinition, [first, second]);

    private static CliTypeIdentity Box(CliTypeIdentity argument) =>
        CliTypeIdentity.GenericInstantiation(BoxDefinition, [argument]);

    [Fact]
    public void AnInstantiationTheCodeWritesOutIsNeverCut()
    {
        var session = Cutoff(limit: 1, breadth: 1).Create();

        session.Admit(Instance(Other, 0), []);

        Assert.Null(session.Admit(Instance(Grow, 9), [Name(Other, 0)]));
        Assert.Null(session.Admit(Instance(Grow, 9, extra: true), []));
    }

    [Fact]
    public void BreadthLimitsTheGrownInstancesOfADefinition()
    {
        var session = Cutoff(limit: 9, breadth: 2).Create();
        session.Admit(Instance(Grow, 0), []);
        session.Admit(Instance(Grow, 0, extra: true), []);

        // Instances that have not grown do not count.
        Assert.Null(session.Admit(Instance(Grow, 1), [Name(Grow, 0)]));
        Assert.Null(session.Admit(Instance(Grow, 2), [Name(Grow, 1)]));
        Assert.NotNull(session.Admit(Instance(Grow, 1, extra: true), [Name(Grow, 0, extra: true)]));
        // Another definition has its own count.
        session.Admit(Instance(Other, 0), []);
        Assert.Null(session.Admit(Instance(Other, 1), [Name(Other, 0)]));
    }

    // Every async method passes through its builder's Start<TStateMachine>, whose code never names the builder's
    // TResult. After a Task<Exception> method has reached it, async methods returning (T, Exception) reach it again with
    // a TResult that contains the earlier one, once for each T: TUnit.Assertions' EvaluationContext<T>.GetAsync does, for
    // every value type a test suite asserts on. That is not growth through Start.
    [Fact]
    public void GrowthInATypeParameterTheCodeDoesNotNameIsNotACycle()
    {
        var session = Cutoff(4, 2, (Grow, [])).Create();
        var start = Instance(Grow, Exception);
        session.Admit(start, []);

        foreach (var value in new[] { Int, Long, CliTypeIdentity.Named(Assembly, "Fixtures", "Result", false) })
        {
            Assert.Null(session.Admit(Instance(Grow, Tuple(value)), [start.CanonicalName]));
        }
    }

    [Fact]
    public void GrowthInATypeParameterTheCodeNamesStillCounts()
    {
        var session = Cutoff(4, 2, (Grow, [CliTypeIdentity.GenericParameter(method: false, 0)])).Create();
        var start = Instance(Grow, Exception);
        session.Admit(start, []);

        Assert.Null(session.Admit(Instance(Grow, Tuple(Int)), [start.CanonicalName]));
        Assert.Null(session.Admit(Instance(Grow, Tuple(Long)), [start.CanonicalName]));
        Assert.NotNull(session.Admit(Instance(Grow, Tuple(Exception)), [start.CanonicalName]));
    }

    // Code names a type parameter through any member or type it uses, and inside any type built from it.
    [Theory]
    [InlineData("array")]
    [InlineData("rectangular array")]
    [InlineData("by-reference")]
    [InlineData("pointer")]
    [InlineData("modified")]
    [InlineData("exact type")]
    [InlineData("field")]
    [InlineData("callee")]
    [InlineData("callee argument")]
    public void CodeNamesATypeParameterThroughEveryOperandAndShape(string naming)
    {
        var parameter = CliTypeIdentity.GenericParameter(method: false, 0);
        var box = CliTypeIdentity.GenericInstantiation(BoxDefinition, [parameter]);
        CilOperand operand = naming switch
        {
            "array" => new CilOperand.TypeIdentity(CliTypeIdentity.SzArray(parameter)),
            "rectangular array" => new CilOperand.TypeIdentity(CliTypeIdentity.Array(parameter, 2)),
            "by-reference" => new CilOperand.TypeIdentity(CliTypeIdentity.ManagedByReference(parameter)),
            "pointer" => new CilOperand.TypeIdentity(CliTypeIdentity.UnmanagedPointer(parameter)),
            "modified" => new CilOperand.TypeIdentity(CliTypeIdentity.Modified(parameter, Exception, isRequired: true)),
            "exact type" => new CilOperand.ExactType(parameter),
            "field" => new CilOperand.FieldInstance(new(
                new(new(Assembly, 0x04000001), new(Assembly, 0x02000001), "Value", CliValueKind.I4, false), box, Int)),
            "callee" => new CilOperand.MethodInstance(new(Other, box, [], Signature)),
            _ => new CilOperand.MethodInstance(new(Other, Exception, [parameter], Signature)),
        };

        Assert.True(Grows(Using(4, 2, (Grow, [operand])).Create()));
    }

    // A function pointer's signature, a primitive and the method's own type parameter (Grow has none) name none of
    // the declaring type's.
    [Fact]
    public void AFunctionPointerOrPrimitiveNamesNoTypeParameter()
    {
        var parameter = CliTypeIdentity.GenericParameter(method: false, 0);
        CilOperand[] operands =
        [
            new CilOperand.TypeIdentity(CliTypeIdentity.FunctionPointer(new(0, 0, 0, new MethodSignatureModel(parameter, [])))),
            new CilOperand.TypeIdentity(Int),
            new CilOperand.TypeIdentity(CliTypeIdentity.GenericParameter(method: true, 0)),
        ];

        Assert.False(Grows(Using(4, 2, (Grow, operands)).Create()));
    }

    // Grow on Box<(T, Exception)> for three T after Box<Exception>, under a breadth of two: the third is cut when the
    // code names T.
    private static bool Grows(IGenericCycleSession session)
    {
        var start = Instance(Grow, Exception);
        session.Admit(start, []);
        Assert.Null(session.Admit(Instance(Grow, Tuple(Int)), [start.CanonicalName]));
        Assert.Null(session.Admit(Instance(Grow, Tuple(Long)), [start.CanonicalName]));
        return session.Admit(Instance(Grow, Tuple(Exception)), [start.CanonicalName]) is not null;
    }

    [Fact]
    public void TheRouteWithTheMostGrowthCounts()
    {
        var session = Cutoff(limit: 2).Create();
        session.Admit(Instance(Grow, 0), []);
        session.Admit(Instance(Grow, 1), [Name(Grow, 0)]);
        session.Admit(Instance(Other, 0), []);

        // Through Other it has not grown; through Grow's second level it has grown twice.
        Assert.NotNull(session.Admit(Instance(Grow, 2), [Name(Other, 0), Name(Grow, 1), "unrecorded"]));
    }

    [Fact]
    public void SessionsAreIndependent()
    {
        var cutoff = Cutoff(limit: 1);
        var first = cutoff.Create();
        first.Admit(Instance(Grow, 0), []);

        Assert.Null(cutoff.Create().Admit(Instance(Grow, 1), [Name(Grow, 0)]));
    }

    [Fact]
    public void RejectsMissingCollaboratorsAndInvalidBounds()
    {
        var types = new Types([]);
        Assert.Throws<ArgumentNullException>(() => new GenericCycleSessionFactory(null!, types, types, types, types));
        Assert.Throws<ArgumentNullException>(() => new GenericCycleSessionFactory(types, null!, types, types, types));
        Assert.Throws<ArgumentNullException>(() => new GenericCycleSessionFactory(types, types, null!, types, types));
        Assert.Throws<ArgumentNullException>(() => new GenericCycleSessionFactory(types, types, types, null!, types));
        Assert.Throws<ArgumentNullException>(() => new GenericCycleSessionFactory(types, types, types, types, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GenericCycleSessionFactory(types, types, types, types, types, limit: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GenericCycleSessionFactory(types, types, types, types, types, breadth: 0));
        var session = Cutoff(limit: 1).Create();
        Assert.Throws<ArgumentNullException>(() => session.Admit(null!, []));
        Assert.Throws<ArgumentNullException>(() => session.Admit(Instance(Grow, 0), null!));
    }

    private static GenericCycleSessionFactory Cutoff(int limit = GenericCycleSessionFactory.DefaultLimit,
        int breadth = GenericCycleSessionFactory.DefaultBreadth, params (MethodDefinitionModel, CliTypeIdentity[])[] bodies) =>
        Using(limit, breadth, [.. bodies.Select(static body =>
            (body.Item1, body.Item2.Select(static CilOperand (type) => new CilOperand.TypeIdentity(type)).ToArray()))]);

    // Bodies that use the operands given.
    private static GenericCycleSessionFactory Using(int limit, int breadth, params (MethodDefinitionModel, CilOperand[])[] bodies)
    {
        var types = new Types(bodies);
        return new(types, types, types, types, types, limit, breadth);
    }

    private static MethodDefinitionModel Definition(int token, string name) =>
        new(new EntityKey(Assembly, token), new EntityKey(Assembly, 0x02000001), name, false, Signature, 0);

    // The definition on Box<...<int>...>, nested depth + 1 times; extra puts long in place of int, another instance
    // of the same size.
    private static MethodInstanceModel Instance(MethodDefinitionModel definition, int depth, bool extra = false)
    {
        var type = extra ? Long : Int;
        for (var level = 0; level <= depth; level++)
        {
            type = CliTypeIdentity.GenericInstantiation(BoxDefinition, [type]);
        }
        return new(definition, type, [], Signature);
    }

    private static readonly CliTypeIdentity Exception = CliTypeIdentity.Named(Assembly, "System", "Exception", false);

    private static CliTypeIdentity Tuple(CliTypeIdentity value) => CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(Assembly, "System", "ValueTuple`2", true), [value, Exception]);

    private static MethodInstanceModel Instance(MethodDefinitionModel definition, CliTypeIdentity argument) =>
        new(definition, CliTypeIdentity.GenericInstantiation(BoxDefinition, [argument]), [], Signature);

    private static string Name(MethodDefinitionModel definition, int depth, bool extra = false) =>
        Instance(definition, depth, extra).CanonicalName;

    // Reads a body only for the definitions a test gives one: each loads the tokens of the types listed.
    private sealed class Types((MethodDefinitionModel Method, CilOperand[] Operands)[] bodies) :
        ITypeFinder, IMethodRepository, IMethodInstanceResolver, ISymbolFormatter, ICalleeMethodBodyReader
    {
        private static readonly MethodSignatureModel ConstructorSignature =
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.ManagedReference);
        private static readonly MethodDefinitionModel Constructor =
            new(ExceptionConstructor, ExceptionType, ".ctor", false, ConstructorSignature, 0);

        public TypeDefinitionModel FindType(string fullName) =>
            fullName == "System.NotSupportedException"
                ? new(ExceptionType, "System", "NotSupportedException", false, [], [ExceptionConstructor])
                : throw new InvalidOperationException(fullName);

        public MethodDefinitionModel GetMethod(EntityKey key) =>
            key == ExceptionConstructor ? Constructor : throw new InvalidOperationException(key.ToString());

        public MethodInstanceModel ResolveMethodInstance(AssemblyIdentity source, int metadataToken,
            string methodDisplayName, int ilOffset, CliGenericContext? genericContext = null) =>
            metadataToken == ExceptionConstructor.MetadataToken
                ? new(Constructor, CliTypeIdentity.Named(Assembly, "System", "NotSupportedException", false), [],
                    ConstructorSignature)
                : throw new InvalidOperationException(methodDisplayName);

        public string Format(EntityKey key) => key.ToString();

        public string Format(MethodDefinitionModel method) => method.Name;

        public bool TryReadMethodBody(MethodDefinitionModel method, [NotNullWhen(true)] out CilMethodBody? body)
        {
            var match = bodies.FirstOrDefault(entry => entry.Method.Key == method.Key);
            body = match.Method is null
                ? null
                : new CilMethodBody(method, 1, [],
                [
                    .. match.Operands.Select((operand, index) =>
                        new CilInstruction(index, index + 1, CilOperation.LoadTypeToken, operand)),
                    new CilInstruction(match.Operands.Length, match.Operands.Length + 1, CilOperation.Return, new CilOperand.None()),
                ]);
            return body is not null;
        }

        public bool TryReadMethodBody(MethodInstanceModel method, [NotNullWhen(true)] out CilMethodBody? body) =>
            throw new InvalidOperationException(method.CanonicalName);
    }
}
