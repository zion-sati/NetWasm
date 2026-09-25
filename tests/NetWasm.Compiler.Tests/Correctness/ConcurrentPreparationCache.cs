using System.Collections.Concurrent;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class ConcurrentPreparationCache<TKey, TValue>(IEqualityComparer<TKey>? comparer = null)
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Lazy<TValue>> _values = new(comparer);

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);

        return _values.GetOrAdd(
            key,
            static (cacheKey, valueFactory) => new Lazy<TValue>(
                () => valueFactory(cacheKey),
                LazyThreadSafetyMode.ExecutionAndPublication),
            prepare).Value;
    }
}
