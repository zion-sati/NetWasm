using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal static class OracleInputBatching
{
    internal const int MaxInputsPerProcess = 100;

    public static IEnumerable<ImmutableArray<int>> Partition(
        ImmutableArray<int> inputs)
    {
        for (var offset = 0; offset < inputs.Length; offset += MaxInputsPerProcess)
        {
            var length = Math.Min(MaxInputsPerProcess, inputs.Length - offset);
            yield return inputs.AsSpan().Slice(offset, length).ToArray().ToImmutableArray();
        }
    }

    public static (ImmutableArray<int> Left, ImmutableArray<int> Right) Bisect(
        ImmutableArray<int> inputs)
    {
        if (inputs.Length < 2)
        {
            throw new ArgumentException("At least two inputs are required", nameof(inputs));
        }

        var midpoint = inputs.Length / 2;
        return (
            inputs.AsSpan().Slice(0, midpoint).ToArray().ToImmutableArray(),
            inputs.AsSpan().Slice(midpoint).ToArray().ToImmutableArray());
    }
}
