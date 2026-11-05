using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class DispatchCandidateIndexTests
{
    private static readonly AssemblyIdentity Assembly = new("Tests");

    [Fact]
    public void IndexMatchesDeclarationPublishedBeforeReceiver()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DispatchCandidateIndexFactory(null!, UnboundedTypeRelationshipKeys.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new DispatchCandidateIndexFactory(new TestTypeRelationshipClassifier(), null!));
        var relationships = new TestTypeRelationshipClassifier();
        var index = CreateIndex(relationships).Index;
        var contract = Type("IContract");
        var receiver = Type("Implementation");
        var declaration = Declaration("first", 1, contract);

        Assert.Empty(index.Index(new DispatchDeclarationCandidate(
            "first",
            declaration)));

        var candidate = Assert.Single(index.Index(
            new DispatchReceiverCandidate(receiver)));
        Assert.Equal("first", candidate.DispatchKey);
        Assert.Same(declaration, candidate.Declaration);
        Assert.Same(receiver, candidate.Receiver);
        Assert.Equal(1, relationships.CallCount);
    }

    [Fact]
    public void IndexMatchesReceiverPublishedBeforeDeclaration()
    {
        var relationships = new TestTypeRelationshipClassifier();
        var index = CreateIndex(relationships).Index;
        var contract = Type("IContract");
        var receiver = Type("Implementation");
        var declaration = Declaration("first", 1, contract);

        Assert.Empty(index.Index(new DispatchReceiverCandidate(receiver)));

        var candidate = Assert.Single(index.Index(
            new DispatchDeclarationCandidate("first", declaration)));
        Assert.Equal("first", candidate.DispatchKey);
        Assert.Same(declaration, candidate.Declaration);
        Assert.Same(receiver, candidate.Receiver);
        Assert.Equal(1, relationships.CallCount);
    }

    [Fact]
    public void IndexRejectsIncompatibleReceiverContractPairs()
    {
        var relationships = new TestTypeRelationshipClassifier(
            isAssignable: false);
        var index = CreateIndex(relationships).Index;
        var contract = Type("IContract");
        var receiver = Type("Implementation");

        Assert.Empty(index.Index(new DispatchDeclarationCandidate(
            "first",
            Declaration("first", 1, contract))));
        Assert.Empty(index.Index(new DispatchReceiverCandidate(receiver)));
        Assert.Equal(1, relationships.CallCount);
    }

    [Fact]
    public void IndexClassifiesEachReceiverContractOnceAndPreservesDeclarationOrder()
    {
        var relationships = new TestTypeRelationshipClassifier();
        var index = CreateIndex(relationships).Index;
        var firstContract = Type("IFirst");
        var secondContract = Type("ISecond");

        index.Index(new DispatchDeclarationCandidate(
            "first",
            Declaration("first", 1, firstContract)));
        index.Index(new DispatchDeclarationCandidate(
            "second",
            Declaration("second", 2, secondContract)));
        index.Index(new DispatchDeclarationCandidate(
            "third",
            Declaration("third", 3, firstContract)));

        var candidates = index.Index(
            new DispatchReceiverCandidate(Type("Implementation")));

        Assert.Equal(["first", "second", "third"],
            candidates.Select(candidate => candidate.DispatchKey));
        Assert.Equal(2, relationships.CallCount);
    }

    [Fact]
    public void IndexPreservesReceiverOrderForLaterDeclarations()
    {
        var relationships = new TestTypeRelationshipClassifier();
        var index = CreateIndex(relationships).Index;
        var first = Type("FirstImplementation");
        var second = Type("SecondImplementation");

        index.Index(new DispatchReceiverCandidate(first));
        index.Index(new DispatchReceiverCandidate(second));

        var candidates = index.Index(new DispatchDeclarationCandidate(
            "first",
            Declaration("first", 1, Type("IContract"))));

        Assert.Equal([first, second],
            candidates.Select(candidate => candidate.Receiver));
        Assert.Equal(2, relationships.CallCount);
    }

    [Fact]
    public void IndexIgnoresExactDuplicatesAndRejectsConflictingKeys()
    {
        var relationships = new TestTypeRelationshipClassifier();
        var index = CreateIndex(relationships).Index;
        var contract = Type("IContract");
        var receiver = Type("Implementation");
        var declaration = Declaration("first", 1, contract);
        var candidate = new DispatchDeclarationCandidate("first", declaration);

        Assert.Empty(index.Index(candidate));
        Assert.Empty(index.Index(candidate));
        Assert.Single(index.Index(new DispatchReceiverCandidate(receiver)));
        Assert.Empty(index.Index(new DispatchReceiverCandidate(receiver)));

        Assert.Throws<InvalidOperationException>(() => index.Index(
            new DispatchDeclarationCandidate(
                "first",
                Declaration("other", 2, contract))));

        Assert.Single(index.Index(
            new DispatchReceiverCandidate(Type("LaterImplementation"))));
        Assert.Equal(2, relationships.CallCount);
    }

    [Fact]
    public void IndexValidatesInputsBeforePublishingState()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DispatchCandidateIndex(null!, UnboundedTypeRelationshipKeys.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new DispatchCandidateIndex(new TestTypeRelationshipClassifier(), null!));

        var relationships = new TestTypeRelationshipClassifier();
        var index = CreateIndex(relationships).Index;
        var declaration = Declaration("first", 1, Type("IContract"));

        Assert.Throws<ArgumentException>(() => index.Index(
            new DispatchDeclarationCandidate("", declaration)));
        Assert.Throws<ArgumentNullException>(() => index.Index(
            new DispatchDeclarationCandidate("first", null!)));
        Assert.Throws<ArgumentNullException>(() => index.Index(
            new DispatchReceiverCandidate(null!)));

        Assert.Empty(index.Index(new DispatchDeclarationCandidate(
            "first",
            declaration)));
        Assert.Equal(0, relationships.CallCount);
    }

    [Fact]
    public void IndexDoesNotPublishReceiverWhenClassificationFails()
    {
        var relationships = new TestTypeRelationshipClassifier(
            failuresRemaining: 1);
        var index = CreateIndex(relationships).Index;
        var receiver = Type("Implementation");
        index.Index(new DispatchDeclarationCandidate(
            "first",
            Declaration("first", 1, Type("IContract"))));

        Assert.Throws<InvalidOperationException>(() => index.Index(
            new DispatchReceiverCandidate(receiver)));

        Assert.Single(index.Index(new DispatchReceiverCandidate(receiver)));
        Assert.Equal(2, relationships.CallCount);
    }

    [Fact]
    public void IndexDoesNotPublishDeclarationWhenClassificationFails()
    {
        var relationships = new TestTypeRelationshipClassifier(
            failuresRemaining: 1);
        var index = CreateIndex(relationships).Index;
        var candidate = new DispatchDeclarationCandidate(
            "first",
            Declaration("first", 1, Type("IContract")));
        index.Index(new DispatchReceiverCandidate(Type("Implementation")));

        Assert.Throws<InvalidOperationException>(() => index.Index(candidate));

        Assert.Single(index.Index(candidate));
        Assert.Equal(2, relationships.CallCount);
    }

    [Fact]
    public void FactoryCreatesIndependentIndexesForSeparateAnalyses()
    {
        var relationships = new TestTypeRelationshipClassifier();
        var factory = CreateFactory(relationships).Factory;
        var candidate = new DispatchDeclarationCandidate(
            "first",
            Declaration("first", 1, Type("IContract")));

        Assert.Empty(factory.Create().Index(candidate));
        Assert.Empty(factory.Create().Index(candidate));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReceiverBoundAdmitsOnlyReceiversAssignableToIt(bool receiversFirst)
    {
        var contract = Type("IEnumerator");
        var bound = Type("IEnumeratorOfA");
        var ofA = Type("EnumeratorOfA");
        // Assignable to the bound only through generic variance.
        var ofDerived = Type("EnumeratorOfDerived");
        var ofB = Type("EnumeratorOfB");
        var relationships = new TableRelationshipClassifier(new()
        {
            [(ofA, contract)] = TypeRelationshipCharacteristics.Hierarchy,
            [(ofDerived, contract)] = TypeRelationshipCharacteristics.Hierarchy,
            [(ofB, contract)] = TypeRelationshipCharacteristics.Hierarchy,
            [(ofA, bound)] = TypeRelationshipCharacteristics.Hierarchy,
            [(ofDerived, bound)] = TypeRelationshipCharacteristics.GenericVariance,
        });
        var index = new DispatchCandidateIndex(relationships, UnboundedTypeRelationshipKeys.Instance);
        var bounded = Declaration("MoveNext", 1, contract) with { ReceiverBound = bound };
        var unbounded = Declaration("Reset", 2, contract);
        CliTypeIdentity[] receivers = [ofA, ofDerived, ofB];

        DispatchCandidate[] candidates = receiversFirst
            ? [.. receivers.SelectMany(receiver => index.Index(new DispatchReceiverCandidate(receiver))),
                .. index.Index(new DispatchDeclarationCandidate("bounded", bounded)),
                .. index.Index(new DispatchDeclarationCandidate("unbounded", unbounded))]
            : [.. index.Index(new DispatchDeclarationCandidate("bounded", bounded)),
                .. index.Index(new DispatchDeclarationCandidate("unbounded", unbounded)),
                .. receivers.SelectMany(receiver => index.Index(new DispatchReceiverCandidate(receiver)))];

        Assert.Equal([ofA, ofDerived],
            candidates.Where(static candidate => candidate.DispatchKey == "bounded").Select(static candidate => candidate.Receiver));
        Assert.Equal(receivers,
            candidates.Where(static candidate => candidate.DispatchKey == "unbounded").Select(static candidate => candidate.Receiver));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassifiesOnlyReceiversAndContractsTheirKeysCanMatch(bool receiversFirst)
    {
        var contractA = Type("ContractA");
        var contractB = Type("ContractB");
        var openContract = Type("OpenContract");
        var onlyA = Type("OnlyA");
        var onlyB = Type("OnlyB");
        var open = Type("Open");
        var both = Type("Both");
        var keys = new KeyTable(new()
        {
            [contractA] = new("A", new HashSet<string> { "A" }, false),
            [contractB] = new("B", new HashSet<string> { "B" }, false),
            [openContract] = new("open", new HashSet<string>(), true),
            [onlyA] = new("onlyA", new HashSet<string> { "onlyA", "A" }, false),
            [onlyB] = new("onlyB", new HashSet<string> { "onlyB", "B" }, false),
            [open] = new("open", new HashSet<string>(), true),
            [both] = new("both", new HashSet<string> { "both", "A", "B" }, false),
        });
        var relationships = new AcceptingRelationshipClassifier();
        var index = new DispatchCandidateIndex(relationships, keys);
        CliTypeIdentity[] receivers = [onlyA, onlyB, open, both];
        DispatchDeclarationCandidate[] declarations =
        [
            new("a", Declaration("A", 1, contractA)),
            new("b", Declaration("B", 2, contractB)),
            new("open", Declaration("Open", 3, openContract)),
        ];

        DispatchCandidate[] candidates = receiversFirst
            ? [.. receivers.SelectMany(receiver => index.Index(new DispatchReceiverCandidate(receiver))),
                .. declarations.SelectMany(declaration => index.Index(declaration))]
            : [.. declarations.SelectMany(declaration => index.Index(declaration)),
                .. receivers.SelectMany(receiver => index.Index(new DispatchReceiverCandidate(receiver)))];

        IEnumerable<CliTypeIdentity> For(string key) => candidates
            .Where(candidate => candidate.DispatchKey == key)
            .Select(candidate => candidate.Receiver);
        Assert.Equal([onlyA, open, both], For("a"));
        Assert.Equal([onlyB, open, both], For("b"));
        Assert.Equal(receivers, For("open"));
        Assert.DoesNotContain((onlyA, contractB), relationships.Pairs);
        Assert.DoesNotContain((onlyB, contractA), relationships.Pairs);
    }

    private sealed class KeyTable(Dictionary<CliTypeIdentity, TypeRelationshipKeys> keys) : ITypeRelationshipKeyResolver
    {
        public TypeRelationshipKeys Resolve(CliTypeIdentity type) => keys[type];
    }

    private sealed class AcceptingRelationshipClassifier : ITypeRelationshipClassifier
    {
        public List<(CliTypeIdentity Candidate, CliTypeIdentity Target)> Pairs { get; } = [];

        public TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target)
        {
            Pairs.Add((candidate, target));
            return new(TypeRelationshipCharacteristics.Hierarchy);
        }
    }

    private sealed class TableRelationshipClassifier(
        Dictionary<(CliTypeIdentity Candidate, CliTypeIdentity Target), TypeRelationshipCharacteristics> relationships) :
        ITypeRelationshipClassifier
    {
        public TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target) =>
            new(relationships.GetValueOrDefault((candidate, target)));
    }

    private static DispatchDeclaration Declaration(
        string name,
        int token,
        CliTypeIdentity declaringType) => new(
            "Caller::Run",
            token,
            MethodInstance(name, token, declaringType),
            CilOperation.LoadVirtualFunction);

    private static MethodInstanceModel MethodInstance(
        string name,
        int token,
        CliTypeIdentity declaringType)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        return new(
            new MethodDefinitionModel(
                new EntityKey(Assembly, token),
                new EntityKey(Assembly, token + 100),
                name,
                false,
                signature,
                1)
            {
                IsVirtual = true,
            },
            declaringType,
            [],
            signature);
    }

    private static DispatchCandidateIndexFixture CreateIndex(
        TestTypeRelationshipClassifier relationships) =>
        new(new DispatchCandidateIndex(relationships, UnboundedTypeRelationshipKeys.Instance));

    private static DispatchCandidateIndexFactoryFixture CreateFactory(
        TestTypeRelationshipClassifier relationships) =>
        new(new DispatchCandidateIndexFactory(relationships, UnboundedTypeRelationshipKeys.Instance));

    private static CliTypeIdentity Type(string name) =>
        CliTypeIdentity.Named(
            Assembly,
            "Tests",
            name,
            isValueType: false);

    private sealed class DispatchCandidateIndexFixture(
        IDispatchCandidateIndex index)
    {
        public IDispatchCandidateIndex Index { get; } = index;
    }

    private sealed class DispatchCandidateIndexFactoryFixture(
        IDispatchCandidateIndexFactory factory)
    {
        public IDispatchCandidateIndexFactory Factory { get; } = factory;
    }
}
