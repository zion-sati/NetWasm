// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
        Assert.Equal("first", Method(candidate));
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
        Assert.Equal("first", Method(candidate));
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
            candidates.Select(Method));
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
            candidates.Where(static candidate => Method(candidate) == "MoveNext")
                .Select(static candidate => candidate.Receiver));
        Assert.Equal(receivers,
            candidates.Where(static candidate => Method(candidate) == "Reset")
                .Select(static candidate => candidate.Receiver));
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
            .Where(candidate => Method(candidate) == key)
            .Select(candidate => candidate.Receiver);
        Assert.Equal([onlyA, open, both], For("A"));
        Assert.Equal([onlyB, open, both], For("B"));
        Assert.Equal(receivers, For("Open"));
        Assert.DoesNotContain((onlyA, contractB), relationships.Pairs);
        Assert.DoesNotContain((onlyB, contractA), relationships.Pairs);
    }

    // Sites of one slot use (one method, one operation) share its resolutions: a receiver is one candidate however
    // many sites call the slot use, whichever comes first.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SitesOfOneSlotUseResolveEachReceiverOnce(bool receiversFirst)
    {
        var relationships = new TestTypeRelationshipClassifier();
        var index = CreateIndex(relationships).Index;
        var contract = Type("IContract");
        var method = MethodInstance("Run", 1, contract);
        DispatchDeclarationCandidate[] sites =
        [
            new("Caller::A@00000001", new("Caller::A", 1, method, CilOperation.CallVirtual)),
            new("Caller::B@00000002", new("Caller::B", 2, method, CilOperation.CallVirtual)),
            new("Caller::C@00000003", new("Caller::C", 3, method, CilOperation.CallVirtual)),
        ];
        CliTypeIdentity[] receivers = [Type("First"), Type("Second")];

        DispatchCandidate[] candidates = receiversFirst
            ? [.. receivers.SelectMany(receiver => index.Index(new DispatchReceiverCandidate(receiver))),
                .. sites.SelectMany(site => index.Index(site))]
            : [.. sites.SelectMany(site => index.Index(site)),
                .. receivers.SelectMany(receiver => index.Index(new DispatchReceiverCandidate(receiver)))];

        Assert.Equal(receivers, candidates.Select(candidate => candidate.Receiver));
        Assert.All(candidates, candidate =>
        {
            Assert.Equal(new DispatchSlot(CilOperation.CallVirtual, method.CanonicalName), candidate.Slot);
            Assert.Same(sites[0].Declaration, candidate.Declaration);
        });
    }

    // A further bounded site of a slot use adds only the receivers its bound admits and no earlier site's did; an
    // unbounded one adds the rest, and after it no site adds any.
    [Fact]
    public void ALaterSiteOfASlotUseAddsOnlyTheReceiversNoEarlierSiteAdmitted()
    {
        var contract = Type("IContract");
        var narrow = Type("INarrow");
        var wide = Type("IWide");
        var inNarrow = Type("InNarrow");
        var inWide = Type("InWide");
        var other = Type("Other");
        var later = Type("Later");
        var relationships = new TableRelationshipClassifier(new()
        {
            [(later, contract)] = TypeRelationshipCharacteristics.Hierarchy,
            [(inNarrow, contract)] = TypeRelationshipCharacteristics.Hierarchy,
            [(inWide, contract)] = TypeRelationshipCharacteristics.Hierarchy,
            [(other, contract)] = TypeRelationshipCharacteristics.Hierarchy,
            [(inNarrow, narrow)] = TypeRelationshipCharacteristics.Hierarchy,
            [(inNarrow, wide)] = TypeRelationshipCharacteristics.Hierarchy,
            [(inWide, wide)] = TypeRelationshipCharacteristics.Hierarchy,
        });
        var index = new DispatchCandidateIndex(relationships, UnboundedTypeRelationshipKeys.Instance);
        var method = MethodInstance("Run", 1, contract);
        DispatchDeclarationCandidate Site(int offset, CliTypeIdentity? bound) =>
            new($"Caller::Run@{offset:x8}", new("Caller::Run", offset, method, CilOperation.CallVirtual, bound));
        foreach (var receiver in new[] { inNarrow, inWide, other })
            Assert.Empty(index.Index(new DispatchReceiverCandidate(receiver)));

        Assert.Equal([inNarrow], index.Index(Site(1, narrow)).Select(candidate => candidate.Receiver));
        Assert.Empty(index.Index(Site(2, narrow)));
        Assert.Equal([inWide], index.Index(Site(3, wide)).Select(candidate => candidate.Receiver));
        Assert.Equal([other], index.Index(Site(4, null)).Select(candidate => candidate.Receiver));
        Assert.Empty(index.Index(Site(5, Type("IThird"))));
        Assert.Equal([later],
            index.Index(new DispatchReceiverCandidate(later)).Select(candidate => candidate.Receiver));
    }

    // The operation is part of the slot use: a call names its target, and ldvirtftn also makes its target callable.
    [Fact]
    public void EachOperationOnAMethodIsItsOwnSlotUse()
    {
        var index = CreateIndex(new TestTypeRelationshipClassifier()).Index;
        var method = MethodInstance("Run", 1, Type("IContract"));
        index.Index(new DispatchDeclarationCandidate("a", new("Caller::Run", 1, method, CilOperation.CallVirtual)));
        index.Index(new DispatchDeclarationCandidate(
            "b", new("Caller::Run", 2, method, CilOperation.LoadVirtualFunction)));

        Assert.Equal([CilOperation.CallVirtual, CilOperation.LoadVirtualFunction],
            index.Index(new DispatchReceiverCandidate(Type("Implementation")))
                .Select(candidate => candidate.Slot.Operation));
    }

    private static string Method(DispatchCandidate candidate) => candidate.Declaration.Declaration.Definition.Name;

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
