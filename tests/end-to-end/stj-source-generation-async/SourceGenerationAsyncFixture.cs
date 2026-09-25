using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace NetWasm.Tests.StjSourceGeneration.AsyncFixture;

public sealed record AsyncEnvelope(
    string Name,
    int? Count,
    List<int> Values,
    Dictionary<string, int?> Metrics);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AsyncEnvelope))]
[JsonSerializable(typeof(IAsyncEnumerable<int>))]
public partial class AsyncFixtureJsonContext : JsonSerializerContext;

public static class EntryPoint
{
    private static Task<int>? _operation;
    private static int _iteratorDisposeCount;
    private static int _throwBeforeDisposeCount;

    public static int Run(int input) => input >= 10000
        ? ExecutePipeRoundTripAsync(input).GetAwaiter().GetResult()
        : ExecuteAsync(input).GetAwaiter().GetResult();

    public static int Start(int input)
    {
        _operation = ExecuteAsync(input);
        return _operation.IsCompleted ? -900 : 0;
    }

    public static int Observe(int input)
    {
        if (_operation is null || !_operation.IsCompleted)
        {
            return -901;
        }

        return _operation.GetAwaiter().GetResult();
    }

    public static int StartPipeRoundTrip(int input)
    {
        _operation = ExecutePipeRoundTripAsync(input);
        return _operation.IsCompleted ? -900 : 0;
    }

    public static int RunPipeRoundTrip(int input) =>
        ExecutePipeRoundTripAsync(input).GetAwaiter().GetResult();

    public static int ObservePipeRoundTrip(int input)
    {
        if (_operation is null || !_operation.IsCompleted)
        {
            return -901;
        }

        return _operation.GetAwaiter().GetResult();
    }

    public static int StartStreamRoundTrip(int input)
    {
        _operation = ExecuteStreamRoundTripAsync(input);
        return _operation.IsCompleted ? -900 : 0;
    }

    public static int RunStreamRoundTrip(int input) =>
        ExecuteStreamRoundTripAsync(input).GetAwaiter().GetResult();

    public static int StartSequenceRoundTrip(int input)
    {
        _operation = ExecuteSequenceRoundTripAsync(input);
        return _operation.IsCompleted ? -900 : 0;
    }

    public static int RunSequenceRoundTrip(int input) =>
        ExecuteSequenceRoundTripAsync(input).GetAwaiter().GetResult();

    public static int StartSequenceFailures(int input)
    {
        _operation = ExecuteSequenceFailuresAsync(input);
        return _operation.IsCompleted ? -900 : 0;
    }

    public static int RunSequenceFailures(int input) =>
        ExecuteSequenceFailuresAsync(input).GetAwaiter().GetResult();

    public static int ObserveSlice(int input)
    {
        if (_operation is null || !_operation.IsCompleted)
        {
            return -901;
        }

        return _operation.GetAwaiter().GetResult();
    }

    private static async Task<int> ExecuteStreamRoundTripAsync(int input)
    {
        var context = AsyncFixtureJsonContext.Default;
        var expected = CreateEnvelope(input);
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, expected, context.AsyncEnvelope);
        stream.Position = 0;
        var actual = await JsonSerializer.DeserializeAsync(stream, context.AsyncEnvelope);
        return Matches(expected, actual) ? 3000 + input : -20;
    }

    private static async Task<int> ExecuteSequenceRoundTripAsync(int input)
    {
        _iteratorDisposeCount = 0;
        var context = AsyncFixtureJsonContext.Default;
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(
            stream,
            YieldValues(input),
            context.IAsyncEnumerableInt32);
        stream.Position = 0;
        var total = 0;
        await foreach (var value in JsonSerializer.DeserializeAsyncEnumerable(
            stream,
            context.Int32))
        {
            total += value;
        }

        return total == input * 2 + 1 && _iteratorDisposeCount == 1
            ? 4000 + input
            : -21;
    }

    private static async Task<int> ExecuteSequenceFailuresAsync(int input)
    {
        _throwBeforeDisposeCount = 0;
        var context = AsyncFixtureJsonContext.Default;
        try
        {
            using var stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(
                stream,
                new ThrowBeforeSequence(),
                context.IAsyncEnumerableInt32);
            return -22;
        }
        catch (InvalidOperationException exception)
        {
            if (exception.Message != "before-suspension" ||
                _throwBeforeDisposeCount != 1)
            {
                return -23;
            }
        }

        try
        {
            using var stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(
                stream,
                new ThrowingDisposeSequence(input),
                context.IAsyncEnumerableInt32);
            return -24;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message == "dispose-after-suspension"
                ? 5000 + input
                : -25;
        }
    }

    private static async Task<int> ExecutePipeRoundTripAsync(int input)
    {
        var context = AsyncFixtureJsonContext.Default;
        var expected = new AsyncEnvelope(
            "pipe-" + input,
            input % 2 == 0 ? input : null,
            new List<int>(),
            new Dictionary<string, int?>());
        var pipe = new System.IO.Pipelines.Pipe();
        await JsonSerializer.SerializeAsync(pipe.Writer, expected, context.AsyncEnvelope);
        pipe.Writer.Complete();
        var actual = await JsonSerializer.DeserializeAsync(pipe.Reader, context.AsyncEnvelope);
        pipe.Reader.Complete();
        if (actual is null || actual.Name != expected.Name)
        {
            return -1;
        }

        return actual.Count.HasValue
            ? 1000 + actual.Count.GetValueOrDefault()
            : 2000;
    }

    private static async Task<int> ExecuteAsync(int input)
    {
        _iteratorDisposeCount = 0;
        _throwBeforeDisposeCount = 0;
        var context = AsyncFixtureJsonContext.Default;
        var expected = CreateEnvelope(input);

        using (var stream = new MemoryStream())
        {
            await JsonSerializer.SerializeAsync(stream, expected, context.AsyncEnvelope);
            stream.Position = 0;
            var actual = await JsonSerializer.DeserializeAsync(stream, context.AsyncEnvelope);
            if (!Matches(expected, actual))
            {
                return -1;
            }
        }

        var pipe = new System.IO.Pipelines.Pipe();
        await JsonSerializer.SerializeAsync(pipe.Writer, expected, context.AsyncEnvelope);
        pipe.Writer.Complete();
        var pipeActual = await JsonSerializer.DeserializeAsync(pipe.Reader, context.AsyncEnvelope);
        pipe.Reader.Complete();
        if (!Matches(expected, pipeActual))
        {
            return -2;
        }

        using (var stream = new MemoryStream())
        {
            await JsonSerializer.SerializeAsync(
                stream,
                YieldValues(input),
                context.IAsyncEnumerableInt32);
            stream.Position = 0;
            var values = new List<int>();
            await foreach (var value in JsonSerializer.DeserializeAsyncEnumerable(
                stream,
                context.Int32))
            {
                values.Add(value);
            }

            if (values.Count != 2 ||
                values[0] != input ||
                values[1] != input + 1 ||
                _iteratorDisposeCount != 1)
            {
                return -3;
            }
        }

        var sequencePipe = new System.IO.Pipelines.Pipe();
        await JsonSerializer.SerializeAsync(
            sequencePipe.Writer,
            YieldValues(input + 2),
            context.IAsyncEnumerableInt32);
        sequencePipe.Writer.Complete();
        var sequenceTotal = 0;
        await foreach (var value in JsonSerializer.DeserializeAsyncEnumerable(
            sequencePipe.Reader,
            context.Int32))
        {
            sequenceTotal += value;
        }
        sequencePipe.Reader.Complete();
        if (sequenceTotal != input * 2 + 5 || _iteratorDisposeCount != 2)
        {
            return -4;
        }

        try
        {
            using var stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(
                stream,
                new ThrowBeforeSequence(),
                context.IAsyncEnumerableInt32);
            return -5;
        }
        catch (InvalidOperationException exception)
        {
            if (exception.Message != "before-suspension" || _throwBeforeDisposeCount != 1)
            {
                return -6;
            }
        }

        try
        {
            using var stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(
                stream,
                ThrowAfterSuspension(),
                context.IAsyncEnumerableInt32);
            return -7;
        }
        catch (InvalidOperationException exception)
        {
            if (exception.Message != "after-suspension")
            {
                return -8;
            }
        }

        try
        {
            using var stream = new MemoryStream();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await JsonSerializer.SerializeAsync(
                stream,
                CancelAfterSuspension(cancellation.Token),
                context.IAsyncEnumerableInt32,
                cancellationToken: cancellation.Token);
            return -9;
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            using var stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(
                stream,
                new ThrowingDisposeSequence(input),
                context.IAsyncEnumerableInt32);
            return -10;
        }
        catch (InvalidOperationException exception)
        {
            if (exception.Message != "dispose-after-suspension")
            {
                return -11;
            }
        }

        return 2000 + input;
    }

    private static AsyncEnvelope CreateEnvelope(int input) => new(
        "async-" + input,
        input % 2 == 0 ? input : null,
        new List<int> { input, input + 1, input + 2 },
        new Dictionary<string, int?>
        {
            ["input"] = input,
            ["nullable"] = input % 2 == 0 ? null : input + 10,
        });

    private static bool Matches(AsyncEnvelope expected, AsyncEnvelope? actual)
    {
        if (actual is null ||
            actual.Name != expected.Name ||
            actual.Count != expected.Count ||
            actual.Values.Count != expected.Values.Count ||
            actual.Metrics.Count != expected.Metrics.Count)
        {
            return false;
        }

        for (var index = 0; index < expected.Values.Count; index++)
        {
            if (actual.Values[index] != expected.Values[index])
            {
                return false;
            }
        }

        return actual.Metrics["input"] == expected.Metrics["input"] &&
            actual.Metrics["nullable"] == expected.Metrics["nullable"];
    }

    private static async IAsyncEnumerable<int> YieldValues(int input)
    {
        try
        {
            await Task.Yield();
            yield return input;
            await Task.Yield();
            yield return input + 1;
        }
        finally
        {
            await Task.Yield();
            _iteratorDisposeCount++;
        }
    }

    private static async IAsyncEnumerable<int> ThrowAfterSuspension()
    {
        await Task.Yield();
        if (DateTime.UtcNow.Ticks == long.MinValue)
        {
            yield return 0;
        }

        throw new InvalidOperationException("after-suspension");
    }

    private static async IAsyncEnumerable<int> CancelAfterSuspension(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return 0;
    }

    private sealed class ThrowBeforeSequence : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        public int Current => 0;

        public IAsyncEnumerator<int> GetAsyncEnumerator(
            CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync() =>
            throw new InvalidOperationException("before-suspension");

        public ValueTask DisposeAsync()
        {
            _throwBeforeDisposeCount++;
            return default;
        }
    }

    private sealed class ThrowingDisposeSequence(int value) :
        IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        private bool _moved;

        public int Current => value;

        public IAsyncEnumerator<int> GetAsyncEnumerator(
            CancellationToken cancellationToken = default) => this;

        public ValueTask<bool> MoveNextAsync()
        {
            var result = !_moved;
            _moved = true;
            return new(result);
        }

        public ValueTask DisposeAsync() => new(DisposeAfterSuspension());

        private static async Task DisposeAfterSuspension()
        {
            await Task.Yield();
            throw new InvalidOperationException("dispose-after-suspension");
        }
    }
}
