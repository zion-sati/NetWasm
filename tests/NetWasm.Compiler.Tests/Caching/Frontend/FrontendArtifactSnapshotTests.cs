using System.Collections.Immutable;
using System.IO;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Caching.Frontend;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Caching.Frontend;

public sealed class FrontendArtifactSnapshotTests
{
    [Fact]
    public void CaptureAndHydratePreserveCompletedFrontendArtifact()
    {
        var artifact = CreateArtifact();
        var snapshotter = Assert.IsAssignableFrom<IFrontendArtifactSnapshotter>(
            new FrontendArtifactSnapshotter());
        var graphBuilder = new RecordingGraphBuilder(
            new ControlFlowGraphBuilderFactory().Create());
        var hydrator = Assert.IsAssignableFrom<IFrontendArtifactHydrator>(
            FrontendCacheTestFactory.Hydrator(graphBuilder));

        var snapshot = snapshotter.Capture(artifact);
        var hydrated = hydrator.Hydrate(snapshot);

        Assert.Equal(1, graphBuilder.BuildCount);
        Assert.Same(snapshot.Analysis.Body, graphBuilder.LastBody);
        Assert.Equal(artifact.Analysis.Method, hydrated.Analysis.Method);
        Assert.Equal(artifact.Analysis.Body.Body, hydrated.Analysis.Body.Body);
        Assert.Equal(
            artifact.Analysis.Body.ControlFlow.EntryStacks,
            hydrated.Analysis.Body.ControlFlow.EntryStacks);
        Assert.Equal(
            artifact.Analysis.Body.ControlFlow.InstructionEntryStacks,
            hydrated.Analysis.Body.ControlFlow.InstructionEntryStacks);
        Assert.Equal(artifact.Analysis.CatchTypes, hydrated.Analysis.CatchTypes);
        Assert.Equal(artifact.Analysis.Exceptions, hydrated.Analysis.Exceptions);
        Assert.Equal(artifact.Analysis.Instructions, hydrated.Analysis.Instructions);
        Assert.Equal(artifact.StructuredMethod, hydrated.StructuredMethod);
        Assert.NotSame(artifact.StructuredMethod, hydrated.StructuredMethod);
        Assert.NotSame(
            artifact.Analysis.Body.ControlFlow.Graph,
            hydrated.Analysis.Body.ControlFlow.Graph);
        var originalBlocks = artifact.Analysis.Body.ControlFlow.Graph.Blocks;
        var hydratedBlocks = hydrated.Analysis.Body.ControlFlow.Graph.Blocks;
        Assert.Equal(originalBlocks.Length, hydratedBlocks.Length);
        for (var index = 0; index < originalBlocks.Length; index++)
        {
            Assert.Equal(originalBlocks[index].Index, hydratedBlocks[index].Index);
            Assert.Equal(originalBlocks[index].StartOffset, hydratedBlocks[index].StartOffset);
            Assert.Equal(
                originalBlocks[index].Instructions.ToArray(),
                hydratedBlocks[index].Instructions.ToArray());
        }
    }

    [Fact]
    public void SnapshotterRejectsNullContracts()
    {
        var snapshotter = Assert.IsAssignableFrom<IFrontendArtifactSnapshotter>(
            new FrontendArtifactSnapshotter());
        var artifact = CreateArtifact();

        Assert.Throws<ArgumentNullException>(() => snapshotter.Capture(null!));
        Assert.Throws<ArgumentNullException>(() => snapshotter.Capture(
            artifact with { Analysis = null! }));
        Assert.Throws<ArgumentNullException>(() => snapshotter.Capture(
            artifact with { StructuredMethod = null! }));
    }

    [Fact]
    public void HydratorRejectsNullContractsBeforeBuildingGraph()
    {
        var graphBuilder = new RecordingGraphBuilder(
            new ControlFlowGraphBuilderFactory().Create());
        Assert.Throws<ArgumentNullException>(() => new FrontendArtifactHydrator(null!, FrontendCacheTestFactory.StructuredMethods()));
        Assert.Throws<ArgumentNullException>(() =>
            new FrontendArtifactHydrator(graphBuilder, null!));
        var hydrator = Assert.IsAssignableFrom<IFrontendArtifactHydrator>(
            FrontendCacheTestFactory.Hydrator(graphBuilder));
        var snapshot = new FrontendArtifactSnapshotter().Capture(CreateArtifact());

        Assert.Throws<ArgumentNullException>(() => hydrator.Hydrate(null!));
        Assert.Throws<ArgumentNullException>(() => hydrator.Hydrate(
            snapshot with { Analysis = null! }));
        Assert.Throws<ArgumentNullException>(() => hydrator.Hydrate(
            snapshot with { StructuredMethod = null! }));
        Assert.Throws<ArgumentNullException>(() => hydrator.Hydrate(snapshot with
        {
            Analysis = snapshot.Analysis with { Method = null! },
        }));
        Assert.Throws<ArgumentNullException>(() => hydrator.Hydrate(snapshot with
        {
            Analysis = snapshot.Analysis with { Body = null! },
        }));
        Assert.Throws<ArgumentNullException>(() => hydrator.Hydrate(snapshot with
        {
            Analysis = snapshot.Analysis with { Instructions = null! },
        }));

        Assert.Equal(0, graphBuilder.BuildCount);
    }

    [Fact]
    public void CodecRoundTripsBasicArtifactDeterministically()
    {
        var snapshot = new FrontendArtifactSnapshotter().Capture(CreateArtifact());
        var encoder = Assert.IsAssignableFrom<IFrontendArtifactEncoder>(
            new FrontendArtifactEncoder());
        var decoder = Assert.IsAssignableFrom<IFrontendArtifactDecoder>(
            new FrontendArtifactDecoder());

        var payload = encoder.Encode(snapshot);
        var decoded = decoder.Decode(payload);
        var reencoded = encoder.Encode(decoded);

        Assert.NotEmpty(payload);
        Assert.Equal(payload.ToArray(), reencoded.ToArray());
        Assert.Equal(
            snapshot.Analysis.Method.CanonicalName,
            decoded.Analysis.Method.CanonicalName);
        Assert.Equal(snapshot.Analysis.Body.Method.Key, decoded.Analysis.Body.Method.Key);
        Assert.Equal(snapshot.Analysis.Body.MaxStack, decoded.Analysis.Body.MaxStack);
        Assert.Equal(
            snapshot.Analysis.Body.Instructions.ToArray(),
            decoded.Analysis.Body.Instructions.ToArray());
        Assert.Equal(
            snapshot.Analysis.CatchTypes.ToArray(),
            decoded.Analysis.CatchTypes.ToArray());
        Assert.Equal(
            snapshot.Analysis.Exceptions.ToArray(),
            decoded.Analysis.Exceptions.ToArray());
        Assert.Equal(
            snapshot.Analysis.Instructions.Strings.ToArray(),
            decoded.Analysis.Instructions.Strings.ToArray());
        Assert.Equal(
            snapshot.StructuredMethod.Header.Method.Key,
            decoded.StructuredMethod.Header.Method.Key);
        Assert.Equal(
            snapshot.StructuredMethod.Header.Instructions.ToArray(),
            decoded.StructuredMethod.Header.Instructions.ToArray());
        Assert.Equal(snapshot.StructuredMethod.EntryBlock, decoded.StructuredMethod.EntryBlock);
    }

    [Fact]
    public void CodecRejectsInvalidContractsAndEnvelopes()
    {
        var snapshot = new FrontendArtifactSnapshotter().Capture(CreateArtifact());
        var encoder = new FrontendArtifactEncoder();
        var decoder = new FrontendArtifactDecoder();

        Assert.Throws<ArgumentNullException>(() => encoder.Encode(null!));
        Assert.Throws<ArgumentNullException>(() => encoder.Encode(
            snapshot with { Analysis = null! }));
        Assert.Throws<ArgumentNullException>(() => encoder.Encode(
            snapshot with { StructuredMethod = null! }));
        Assert.Throws<ArgumentException>(() => decoder.Decode(default));
        Assert.Throws<InvalidDataException>(() => decoder.Decode([]));

        var payload = encoder.Encode(snapshot).ToArray();
        var invalidMagic = payload.ToArray();
        invalidMagic[0] ^= 0xff;
        Assert.Throws<InvalidDataException>(() => decoder.Decode([.. invalidMagic]));
        var invalidVersion = payload.ToArray();
        invalidVersion[4] ^= 0xff;
        Assert.Throws<InvalidDataException>(() => decoder.Decode([.. invalidVersion]));
        Assert.Throws<InvalidDataException>(() => decoder.Decode([.. payload, 0]));
        Assert.Throws<InvalidDataException>(() => decoder.Decode([.. payload[..^1]]));
    }

    internal static FrontendArtifact CreateArtifact()
    {
        var assembly = new AssemblyIdentity("Dependency");
        var type = new EntityKey(assembly, 0x02000001);
        var definition = new MethodDefinitionModel(
            new EntityKey(assembly, 0x06000001),
            type,
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var instance = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(assembly, "Fixture", "Program", false),
            [],
            definition.Signature);
        var body = new CilMethodBody(
            definition,
            1,
            [],
            [new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None())])
        {
            MethodInstance = instance,
        };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        var entryStacks = graph.Blocks.ToImmutableDictionary(
            block => block.Index,
            _ => ImmutableArray<CliValueKind>.Empty);
        var instructionStacks = body.Instructions.ToImmutableDictionary(
            instruction => instruction.Offset,
            _ => ImmutableArray<CliValueKind>.Empty);
        var validated = new ValidatedControlFlowGraph(
            graph,
            entryStacks,
            instructionStacks);
        var managed = new ManagedMethodBody(instance, validated);
        var structured = new ValidatedStructuredMethodBuilderFactory().Create().Build(validated);
        var analysis = new ReachableMethodAnalysis(
            instance,
            managed,
            [type],
            [new ReachabilityExceptionRequirement(ManagedExceptionKind.NullReference, "System.NullReferenceException")],
            new([], [], [], [type], ["value"], [], [], [], [], []));
        return new(analysis, structured);
    }

    private sealed class RecordingGraphBuilder(IControlFlowGraphBuilder inner) :
        IControlFlowGraphBuilder
    {
        public int BuildCount { get; private set; }

        public CilMethodBody? LastBody { get; private set; }

        public ControlFlowGraph Build(CilMethodBody methodBody)
        {
            BuildCount++;
            LastBody = methodBody;
            return inner.Build(methodBody);
        }
    }
}
