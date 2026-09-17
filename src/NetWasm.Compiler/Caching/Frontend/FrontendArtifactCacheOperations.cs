using NetWasm.Compiler.Analysis;
using System;
using System.IO;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Caching.Frontend;

internal interface IFrontendArtifactRestorer
{
    bool TryRestore(MethodInstanceModel method, out FrontendArtifact artifact);
}

internal interface IFrontendAnalysisRecorder
{
    void Record(ReachableMethodAnalysis analysis);
}

internal interface IFrontendStructuredMethodRestorer
{
    bool TryRestore(MethodInstanceModel method, out StructuredMethod structured);
}

internal interface IFrontendArtifactStager
{
    void Stage(MethodInstanceModel method, StructuredMethod structured);
}

internal sealed class FrontendArtifactRestorer(
    IFrontendArtifactCacheRequestResolver requests,
    IFrontendArtifactObjectReader objects,
    IFrontendArtifactPayloadReader payloads,
    IFrontendArtifactDecoder decoder,
    IFrontendArtifactHydrator hydrator) : IFrontendArtifactRestorer
{
    public bool TryRestore(MethodInstanceModel method, out FrontendArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(method);
        var request = requests.Resolve();
        if (request is null)
        {
            artifact = null!;
            return false;
        }
        System.Threading.Interlocked.Increment(ref request.Lookups);
        var context = request.Context;
        if (context is null || !context.IsEligible(method))
        {
            System.Threading.Interlocked.Increment(ref request.Misses);
            artifact = null!;
            return false;
        }
        var key = context.MethodKey(method);
        if (objects.TryRead(new(context, key), out artifact))
        {
            request.Restored.TryAdd(method.CanonicalName, artifact.StructuredMethod);
            System.Threading.Interlocked.Increment(ref request.Hits);
            System.Threading.Interlocked.Increment(ref request.MemoryHits);
            return true;
        }
        if (!payloads.TryRead(new(context, key), out var payload))
        {
            System.Threading.Interlocked.Increment(ref request.Misses);
            artifact = null!;
            return false;
        }
        try
        {
            artifact = hydrator.Hydrate(decoder.Decode(payload));
            request.Restored.TryAdd(method.CanonicalName, artifact.StructuredMethod);
            System.Threading.Interlocked.Increment(ref request.Hits);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or
            UnauthorizedAccessException or CompilerException or StructuredMethodValidationException)
        {
            System.Threading.Interlocked.Increment(ref request.Misses);
            artifact = null!;
            return false;
        }
    }
}

internal sealed class FrontendAnalysisRecorder(IFrontendArtifactCacheRequestResolver requests) : IFrontendAnalysisRecorder
{
    public void Record(ReachableMethodAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var request = requests.Resolve();
        if (request is null) return;
        if (request.Context is not null) request.Analyses.TryAdd(analysis.Method.CanonicalName, analysis);
    }
}

internal sealed class FrontendStructuredMethodRestorer(IFrontendArtifactCacheRequestResolver requests) : IFrontendStructuredMethodRestorer
{
    public bool TryRestore(MethodInstanceModel method, out StructuredMethod structured)
    {
        ArgumentNullException.ThrowIfNull(method);
        var request = requests.Resolve();
        if (request is null)
        {
            structured = null!;
            return false;
        }
        return request.Restored.TryRemove(method.CanonicalName, out structured!);
    }
}

internal sealed class FrontendArtifactStager(
    IFrontendArtifactCacheRequestResolver requests,
    IFrontendArtifactEligibilityClassifier eligibility,
    IFrontendArtifactSnapshotter snapshotter,
    IFrontendArtifactEncoder encoder) : IFrontendArtifactStager
{
    private const long MaximumRequestBytes = 64L * 1024 * 1024;

    public void Stage(MethodInstanceModel method, StructuredMethod structured)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(structured);
        var request = requests.Resolve();
        if (request is null) return;
        var context = request.Context;
        if (context is null || !request.Analyses.TryRemove(method.CanonicalName, out var analysis) ||
            eligibility.Classify(new(context.EntryAssembly, analysis, structured)) != FrontendArtifactEligibility.Eligible) return;
        var payload = encoder.Encode(snapshotter.Capture(new(analysis, structured)));
        if (payload.Length > FrontendArtifactPayloadReader.MaximumPayloadBytes) return;
        var key = context.MethodKey(method);
        if (request.Staged.TryAdd(key, payload))
        {
            request.StagedObjects.TryAdd(key, new(analysis, structured));
            System.Threading.Interlocked.Increment(ref request.StagedArtifacts);
            var total = System.Threading.Interlocked.Add(ref request.StagedBytes, payload.Length);
            if (total > MaximumRequestBytes && request.Staged.TryRemove(key, out var removed))
                System.Threading.Interlocked.Add(ref request.StagedBytes, -removed.Length);
        }
    }
}
