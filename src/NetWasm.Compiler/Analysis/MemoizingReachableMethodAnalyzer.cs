using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

/// <summary>
/// The analyses of one compile's reachability closures, by method and body. A reducing
/// closure builds the whole program again after each reduction rewrites some bodies; a
/// method's analysis reads only the method, its body as the request gives it, and the
/// compile's fixed metadata, so every method a reduction left alone analyzes as it did.
/// </summary>
internal sealed class ReachableMethodAnalysisMemo
{
    private readonly ConcurrentDictionary<Key, ReachableMethodAnalysis> _analyses = new(KeyComparer.Instance);

    internal int Count => _analyses.Count;

    internal ReachableMethodAnalysis GetOrAdd(ReachableMethodRequest request, Func<ReachableMethodAnalysis> analyze)
    {
        var key = new Key(request.Method.CanonicalName, request.RewrittenBody);
        if (_analyses.TryGetValue(key, out var analysis))
            return analysis;
        analysis = analyze();
        return _analyses.GetOrAdd(key, analysis);
    }

    // A rewritten body is the reduction's own object, so it is compared by reference.
    private readonly record struct Key(string Method, CilMethodBody? Body);

    private sealed class KeyComparer : IEqualityComparer<Key>
    {
        internal static readonly KeyComparer Instance = new();

        public bool Equals(Key x, Key y) =>
            ReferenceEquals(x.Body, y.Body) & string.Equals(x.Method, y.Method, StringComparison.Ordinal);

        public int GetHashCode(Key key) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(key.Method), RuntimeHelpers.GetHashCode(key.Body));
    }
}

/// <summary>Takes a method's analysis from the compile's memo, analyzing it only the first time.</summary>
internal sealed class MemoizingReachableMethodAnalyzer(
    IReachableMethodAnalyzer inner,
    ReachableMethodAnalysisMemo memo) : IReachableMethodAnalyzer
{
    private readonly IReachableMethodAnalyzer _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ReachableMethodAnalysisMemo _memo = memo ?? throw new ArgumentNullException(nameof(memo));

    public ReachableMethodAnalysis Analyze(ReachableMethodRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _memo.GetOrAdd(request, () => _inner.Analyze(request));
    }
}
