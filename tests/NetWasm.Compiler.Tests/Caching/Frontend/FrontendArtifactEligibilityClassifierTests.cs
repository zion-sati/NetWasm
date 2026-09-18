using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Caching.Frontend;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using Structured = NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Tests.Caching.Frontend;

public sealed class FrontendArtifactEligibilityClassifierTests
{
    private static readonly AssemblyIdentity Entry = new("Entry");
    private static readonly AssemblyIdentity Dependency = new("Dependency");

    [Fact]
    public void ClassifyAcceptsDetachedDependencyArtifact()
    {
        var request = CreateRequest();

        var result = Classifier().Classify(request);

        Assert.Equal(FrontendArtifactEligibility.Eligible, result);
    }

    [Fact]
    public void ClassifyRejectsEntryOwnedMethod()
    {
        var request = CreateRequest(Method(Entry));

        var result = Classifier().Classify(request);

        Assert.Equal(FrontendArtifactEligibility.ReferencesEntryAssembly, result);
    }

    [Theory]
    [MemberData(nameof(EntryOperands))]
    public void ClassifyRejectsEntryReferenceInInstruction(CilOperand operand)
    {
        var request = CreateRequest(instructionOperand: operand);

        var result = Classifier().Classify(request);

        Assert.Equal(FrontendArtifactEligibility.ReferencesEntryAssembly, result);
    }

    [Fact]
    public void ClassifyRejectsEntryCatchTypeInStructuredArtifact()
    {
        var request = CreateRequest();
        var id = new NetWasm.Compiler.ControlFlow.Structured.StructuredExceptionGroupId(1);
        var clause = new NetWasm.Compiler.ControlFlow.Structured.StructuredExceptionClause(
            CilExceptionRegionKind.Catch,
            0,
            1,
            Key(Entry, 0x02000002),
            null,
            NetWasm.Compiler.ControlFlow.Structured.StructuredSequence.Empty,
            null)
        {
            HandlerBlock = request.StructuredMethod.EntryBlock,
        };
        var group = new NetWasm.Compiler.ControlFlow.Structured.StructuredExceptionGroup(
            id,
            null,
            0,
            1,
            [],
            [clause],
            [],
            null,
            null);
        var structured = request.StructuredMethod with
        {
            ExceptionGroups = request.StructuredMethod.ExceptionGroups.Add(id, group),
        };

        var result = Classifier().Classify(request with { StructuredMethod = structured });

        Assert.Equal(FrontendArtifactEligibility.ReferencesEntryAssembly, result);
    }

    [Fact]
    public void ClassifyRejectsEntryTypeNestedInDependencyGeneric()
    {
        var entryType = CliTypeIdentity.Named(Entry, "Fixture", "Value", true);
        var generic = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Dependency, "Fixture", "Box`1", false),
            [entryType]);
        var request = CreateRequest(Method(Dependency, generic));

        var result = Classifier().Classify(request);

        Assert.Equal(FrontendArtifactEligibility.ReferencesEntryAssembly, result);
    }

    [Fact]
    public void ClassifyRejectsEntryReferenceInEveryReachabilityFactKind()
    {
        var request = CreateRequest();
        var facts = request.Analysis.Instructions;
        var entryType = CliTypeIdentity.Named(Entry, "Fixture", "Value", true);
        var entryKey = Key(Entry, 0x02000002);
        var entryMethod = Instance(Method(Entry));
        var entryField = EntryField();
        var callSite = new ManagedCallSite(
            new ManagedCallSiteKey(new ManagedMethodIdentity("caller"), 0),
            ManagedCallOperation.Direct,
            new ManagedMethodIdentity(entryMethod.CanonicalName),
            entryMethod,
            entryType);
        var variants = new[]
        {
            request with { Analysis = request.Analysis with { CatchTypes = [entryKey] } },
            WithFacts(request, facts with { RuntimeTypes = [entryType] }),
            WithFacts(request, facts with { ConstructedTypes = [entryType] }),
            WithFacts(request, facts with { AllocatedTypes = [entryType] }),
            WithFacts(request, facts with { Types = [entryKey] }),
            WithFacts(request, facts with
            {
                Methods = [new ReachabilityMethodReference(CilOperation.Call, entryMethod)],
            }),
            WithFacts(request, facts with
            {
                Entities = [new ReachabilityEntityReference(CilOperation.LoadTypeToken, entryKey)],
            }),
            WithFacts(request, facts with { Fields = [entryField] }),
            WithFacts(request, facts with
            {
                Dispatches = [new ReachabilityDispatch(
                    "dispatch",
                    new DispatchDeclaration("caller", 0, entryMethod, CilOperation.CallVirtual))],
            }),
            WithFacts(request, facts with { CallableMethods = [entryMethod] }),
            WithFacts(request, facts with { CallSites = [callSite] }),
        };

        Assert.All(variants, AssertRejected);
    }

    [Fact]
    public void ClassifyRejectsEntryReferenceInStructuredMethodLocations()
    {
        var request = CreateRequest();
        var structured = request.StructuredMethod;
        var entryMethod = Method(Entry);
        var entryInstance = Instance(entryMethod);
        var entryType = CliTypeIdentity.Named(Entry, "Fixture", "Value", true);
        var entryInstruction = EntryInstruction();
        var block = structured.Blocks.Single().Value;
        var variants = new[]
        {
            request with
            {
                StructuredMethod = structured with
                {
                    Header = structured.Header with { Method = entryMethod },
                },
            },
            request with
            {
                StructuredMethod = structured with
                {
                    Header = structured.Header with { MethodInstance = entryInstance },
                },
            },
            request with
            {
                StructuredMethod = structured with
                {
                    Header = structured.Header with { LocalSignatureTypes = [entryType] },
                },
            },
            request with
            {
                StructuredMethod = structured with
                {
                    Header = structured.Header with { Instructions = [entryInstruction] },
                },
            },
            request with
            {
                StructuredMethod = structured with
                {
                    Blocks = structured.Blocks.SetItem(
                        block.Id,
                        block with { Instructions = [entryInstruction] }),
                },
            },
            request with
            {
                StructuredMethod = structured with
                {
                    Blocks = structured.Blocks.SetItem(
                        block.Id,
                        block with { Exit = new Structured.StructuredTerminalExit(entryInstruction) }),
                },
            },
        };

        Assert.All(variants, AssertRejected);
    }

    [Fact]
    public void ClassifyTraversesLaterFieldMethodCallSiteAndTypeLocations()
    {
        var request = CreateRequest();
        var dependencyType = CliTypeIdentity.Named(Dependency, "Fixture", "Owner", false);
        var entryType = CliTypeIdentity.Named(Entry, "Fixture", "Value", true);
        var dependencyMethod = Instance(Method(Dependency));
        var dependencyField = new FieldDefinitionModel(
            Key(Dependency, 0x04000001),
            Key(Dependency, 0x02000001),
            "Value",
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            true);
        var fields = new[]
        {
            new FieldInstanceModel(
                dependencyField with { DeclaringType = Key(Entry, 0x02000001) },
                dependencyType,
                CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            new FieldInstanceModel(
                dependencyField with { SignatureType = entryType },
                dependencyType,
                CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            new FieldInstanceModel(dependencyField, entryType, CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            new FieldInstanceModel(dependencyField, dependencyType, entryType),
        };
        var methodArgument = dependencyMethod with { MethodArguments = [entryType] };
        var methodSignature = dependencyMethod with
        {
            Signature = new MethodSignatureModel(entryType, []),
        };
        var constrainedCall = new ManagedCallSite(
            new ManagedCallSiteKey(new ManagedMethodIdentity("caller"), 0),
            ManagedCallOperation.Direct,
            new ManagedMethodIdentity(dependencyMethod.CanonicalName),
            dependencyMethod,
            entryType);
        var storageType = CliTypeIdentity.Named(
            Dependency,
            "Fixture",
            "Storage",
            true).WithStackStorageType(entryType);
        var variants = fields.Select(field => WithFacts(
                request,
                request.Analysis.Instructions with { Fields = [field] }))
            .Append(WithFacts(request, request.Analysis.Instructions with
            {
                CallableMethods = [methodArgument],
            }))
            .Append(WithFacts(request, request.Analysis.Instructions with
            {
                CallableMethods = [methodSignature],
            }))
            .Append(WithFacts(request, request.Analysis.Instructions with
            {
                CallSites = [constrainedCall],
            }))
            .Append(WithFacts(request, request.Analysis.Instructions with
            {
                RuntimeTypes = [storageType],
            }));

        Assert.All(variants, AssertRejected);
    }

    [Fact]
    public void ClassifyAcceptsNullCatchAndNonTerminalStructuredExit()
    {
        var request = CreateRequest();
        var structured = request.StructuredMethod;
        var block = structured.Blocks.Single().Value;
        var id = new Structured.StructuredExceptionGroupId(1);
        var clause = new Structured.StructuredExceptionClause(
            CilExceptionRegionKind.Finally,
            0,
            1,
            null,
            null,
            Structured.StructuredSequence.Empty,
            null)
        {
            HandlerBlock = block.Id,
        };
        var group = new Structured.StructuredExceptionGroup(
            id, null, 0, 1, [], [clause], [], null, null);
        var candidate = structured with
        {
            Blocks = structured.Blocks.SetItem(
                block.Id,
                block with { Exit = new Structured.StructuredFallthroughExit(null) }),
            ExceptionGroups = structured.ExceptionGroups.Add(id, group),
        };

        var result = Classifier().Classify(request with { StructuredMethod = candidate });

        Assert.Equal(FrontendArtifactEligibility.Eligible, result);
    }

    [Fact]
    public void ClassifyAcceptsCallSiteWithoutConstrainedType()
    {
        var request = CreateRequest();
        var method = request.Analysis.Method;
        var call = new ManagedCallSite(
            new ManagedCallSiteKey(new ManagedMethodIdentity("caller"), 0),
            ManagedCallOperation.Direct,
            new ManagedMethodIdentity(method.CanonicalName),
            method,
            null);

        var result = Classifier().Classify(WithFacts(
            request,
            request.Analysis.Instructions with { CallSites = [call] }));

        Assert.Equal(FrontendArtifactEligibility.Eligible, result);
    }

    [Fact]
    public void ClassifyRejectsEntryReferenceInBodyLocalSignature()
    {
        var request = CreateRequest(
            bodyLocalSignature: CliTypeIdentity.Named(Entry, "Fixture", "Value", true));

        AssertRejected(request);
    }

    [Fact]
    public void ClassifyChecksPresentAndAbsentBodyCatchTypes()
    {
        AssertRejected(CreateRequest(bodyCatchType: Key(Entry, 0x02000002)));

        var result = Classifier().Classify(CreateRequest(addFinallyRegion: true));

        Assert.Equal(FrontendArtifactEligibility.Eligible, result);
    }

    [Fact]
    public void ClassifyRejectsNullContracts()
    {
        var request = CreateRequest();
        var classifier = Classifier();

        Assert.Throws<ArgumentNullException>(() => classifier.Classify(null!));
        Assert.Throws<ArgumentNullException>(() => classifier.Classify(
            request with { Analysis = null! }));
        Assert.Throws<ArgumentNullException>(() => classifier.Classify(
            request with { StructuredMethod = null! }));
    }

    public static TheoryData<CilOperand> EntryOperands() => new()
    {
        new CilOperand.Entity(Key(Entry, 0x06000009)),
        new CilOperand.MethodInstance(Instance(Method(Entry))),
        new CilOperand.FieldInstance(new FieldInstanceModel(
            new FieldDefinitionModel(
                Key(Entry, 0x04000001),
                Key(Entry, 0x02000001),
                "Value",
                CliTypeIdentity.Named(Entry, "Fixture", "Value", true),
                true),
            CliTypeIdentity.Named(Entry, "Fixture", "Owner", false),
            CliTypeIdentity.Named(Entry, "Fixture", "Value", true))),
        new CilOperand.TypeIdentity(
            CliTypeIdentity.Named(Entry, "Fixture", "Value", true)),
        new CilOperand.CallSite(new MethodSignatureModel(
            CliTypeIdentity.Named(Entry, "Fixture", "Value", true),
            [])),
    };

    private static FrontendArtifactEligibilityClassifier Classifier() =>
        new FrontendArtifactEligibilityClassifier();

    private static FrontendArtifactEligibilityRequest CreateRequest(
        MethodDefinitionModel? definition = null,
        CilOperand? instructionOperand = null,
        CliTypeIdentity? bodyLocalSignature = null,
        EntityKey? bodyCatchType = null,
        bool addFinallyRegion = false)
    {
        definition ??= Method(Dependency);
        var instance = Instance(definition);
        var hasRegion = bodyCatchType is not null || addFinallyRegion;
        var instructions = hasRegion
            ? ImmutableArray.Create(
                new CilInstruction(0, 1, CilOperation.Leave, new CilOperand.BranchTarget(2)),
                new CilInstruction(
                    1,
                    2,
                    addFinallyRegion ? CilOperation.EndFinally : CilOperation.Leave,
                    addFinallyRegion ? new CilOperand.None() : new CilOperand.BranchTarget(2)),
                new CilInstruction(2, 3, CilOperation.Return, new CilOperand.None()))
            : instructionOperand is null
                ? [new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None())]
                : [
                    new CilInstruction(0, 1, CilOperation.Nop, instructionOperand),
                    new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None()),
                ];
        var body = new CilMethodBody(
            definition,
            1,
            [],
            instructions)
        {
            MethodInstance = instance,
            LocalSignatureTypes = bodyLocalSignature is null ? [] : [bodyLocalSignature],
            ExceptionRegions = hasRegion
                ? [new CilExceptionRegion(
                    bodyCatchType is null ? CilExceptionRegionKind.Finally : CilExceptionRegionKind.Catch,
                    0,
                    1,
                    1,
                    1,
                    bodyCatchType,
                    null)]
                : [],
        };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        var validated = new ValidatedControlFlowGraph(
            graph,
            graph.Blocks.ToImmutableDictionary(
                block => block.Index,
                _ => ImmutableArray<CliValueKind>.Empty),
            graph.Blocks.SelectMany(block => block.Instructions).ToImmutableDictionary(
                item => item.Offset,
                _ => ImmutableArray<CliValueKind>.Empty));
        var managed = new ManagedMethodBody(instance, validated);
        var structured = new ValidatedStructuredMethodBuilderFactory().Create().Build(validated);
        var analysis = new ReachableMethodAnalysis(
            instance,
            managed,
            [],
            [],
            new([], [], [], [], [], [], [], [], [], []));
        return new(Entry, analysis, structured);
    }

    private static FrontendArtifactEligibilityRequest WithFacts(
        FrontendArtifactEligibilityRequest request,
        ReachabilityInstructionAnalysis facts) => request with
        {
            Analysis = request.Analysis with { Instructions = facts },
        };

    private static void AssertRejected(FrontendArtifactEligibilityRequest request) =>
        Assert.Equal(
            FrontendArtifactEligibility.ReferencesEntryAssembly,
            Classifier().Classify(request));

    private static CilInstruction EntryInstruction() => new(
        0,
        1,
        CilOperation.LoadTypeToken,
        new CilOperand.Entity(Key(Entry, 0x02000002)));

    private static FieldInstanceModel EntryField()
    {
        var type = CliTypeIdentity.Named(Entry, "Fixture", "Value", true);
        return new(
            new FieldDefinitionModel(
                Key(Entry, 0x04000001),
                Key(Entry, 0x02000001),
                "Value",
                type,
                true),
            CliTypeIdentity.Named(Entry, "Fixture", "Owner", false),
            type);
    }

    private static MethodDefinitionModel Method(
        AssemblyIdentity assembly,
        CliTypeIdentity? returnType = null) => new(
        Key(assembly, 0x06000001),
        Key(assembly, 0x02000001),
        "Run",
        true,
        new MethodSignatureModel(
            returnType ?? CliTypeIdentity.FromStackKind(CliValueKind.Void),
            []),
        1);

    private static MethodInstanceModel Instance(MethodDefinitionModel method) => new(
        method,
        CliTypeIdentity.Named(method.Key.Assembly, "Fixture", "Owner", false),
        [],
        method.Signature);

    private static EntityKey Key(AssemblyIdentity assembly, int token) => new(assembly, token);
}
