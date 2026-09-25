using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AsyncCompilationTests
{
    [Fact]
    public void UnobservedTaskFailureRetainsRuntimeDiagnosticIntrinsic()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "UnobservedTaskDiagnosticFixture",
            """
            using System.Threading.Tasks;

            namespace UnobservedTaskDiagnosticFixture;

            public static class EntryPoint
            {
                private static Task<int>? _task;

                public static int Run(int input)
                {
                    _task = Task<int>.FromException(new System.InvalidOperationException());
                    _task = null;
                    return input;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(CreateAsyncOptions(
            assets,
            assembly,
            "UnobservedTaskDiagnosticFixture.EntryPoint",
            "Run"));

        Assert.Empty(result.Program.JSImportMethods);
        Assert.Contains(
            "report_unobserved_task_exception",
            System.Text.Encoding.UTF8.GetString(result.ApplicationModule),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerResumesSourceBackedValueTaskForEveryProfile(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Threading.Tasks;
            using System.Threading.Tasks.Sources;

            namespace ValueTaskSourceFixture;

            public sealed class Source : IValueTaskSource<int>
            {
                private ManualResetValueTaskSourceCore<int> _core;

                public ValueTask<int> Task => new(this, _core.Version);
                public void Complete(int value) => _core.SetResult(value);
                public int GetResult(short token) => _core.GetResult(token);
                public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
                public void OnCompleted(
                    Action<object?> continuation,
                    object? state,
                    short token,
                    ValueTaskSourceOnCompletedFlags flags) =>
                    _core.OnCompleted(continuation, state, token, flags);
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var source = new Source();
                    var pending = Read(source.Task);
                    if (pending.IsCompleted)
                    {
                        return -1;
                    }
                    source.Complete(input + 1);
                    return pending.Result;
                }

                private static async Task<int> Read(ValueTask<int> value) => await value;
            }
            """;
        var assembly = optimized
            ? assets.CompileOptimizedSource("ValueTaskSourceFixture", source)
            : assets.CompileSource("ValueTaskSourceFixture", source);

        var result = NetWasmCompiler.Compile(CreateAsyncOptions(
            assets,
            assembly,
            "ValueTaskSourceFixture.EntryPoint",
            "Run",
            target));

        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            41,
            target,
            result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerLowersAsyncIteratorControlFlowForEveryProfile(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace AsyncIteratorFixture;

            public static class EntryPoint
            {
                public static int Run(int input) => Consume(input).Result;

                private static async Task<int> Consume(int input)
                {
                    var total = 0;
                    await foreach (var value in Values(input))
                    {
                        total += value;
                    }
                    return total;
                }

                private static async IAsyncEnumerable<int> Values(int input)
                {
                    yield return input;
                    await Task.CompletedTask;
                    if (input < 0)
                    {
                        yield break;
                    }
                    yield return input + 1;
                }
            }
            """;
        var assembly = optimized
            ? assets.CompileOptimizedSource("AsyncIteratorFixture", source)
            : assets.CompileSource("AsyncIteratorFixture", source);

        var result = NetWasmCompiler.Compile(CreateAsyncOptions(
            assets,
            assembly,
            "AsyncIteratorFixture.EntryPoint",
            "Run",
            target));

        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(83, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            41,
            target,
            result.StaticDataEnd));
        Assert.Equal(-1, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            -1,
            target,
            result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerLowersAwaitForeachAndAsyncDisposalForEveryProfile(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            namespace AwaitForeachFixture;

            public sealed class Values : IAsyncEnumerable<int>, IAsyncEnumerator<int>
            {
                private readonly int _start;
                private int _index;
                public static int DisposeCount;

                public Values(int start) => _start = start;
                public int Current => _start + _index;

                public IAsyncEnumerator<int> GetAsyncEnumerator(
                    CancellationToken cancellationToken = default) => this;

                public ValueTask<bool> MoveNextAsync()
                {
                    _index++;
                    return new ValueTask<bool>(_index <= 2);
                }

                public ValueTask DisposeAsync()
                {
                    DisposeCount++;
                    return default;
                }
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Values.DisposeCount = 0;
                    return Consume(input).Result + Values.DisposeCount * 100;
                }

                private static async Task<int> Consume(int input)
                {
                    var total = 0;
                    await foreach (var value in new Values(input))
                    {
                        total += value;
                    }
                    return total;
                }
            }
            """;
        var assembly = optimized
            ? assets.CompileOptimizedSource("AwaitForeachFixture", source)
            : assets.CompileSource("AwaitForeachFixture", source);

        var result = NetWasmCompiler.Compile(CreateAsyncOptions(
            assets,
            assembly,
            "AwaitForeachFixture.EntryPoint",
            "Run",
            target));

        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(185, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            41,
            target,
            result.StaticDataEnd));
    }

    [Fact]
    public void CompilerTranslatesOrdinaryRoslynAsyncStateMachine()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "AsyncCompilerFixture",
            """
            using System.Threading.Tasks;

            namespace AsyncCompilerFixture;

            public static class EntryPoint
            {
                private static Task<int>? _pending;

                public static int Run(int input)
                {
                    _pending = CompleteLater(input);
                    return _pending.IsCompleted ? _pending.Result : -1;
                }

                private static async Task<int> CompleteLater(int input)
                {
                    await Task.Delay(0);
                    return input + 1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "AsyncCompilerFixture.EntryPoint",
            "Run",
            [],
            WitPath: Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform@1.0.0/async-platform"));

        Assert.NotEmpty(result.ApplicationModule);
        Assert.Contains(result.Program.MethodInstances.Values,
            method => method.Definition.Name == "AwaitUnsafeOnCompleted");
        Assert.Contains(result.Program.WitImportMethods,
            method => method.WitImport?.InterfaceName ==
                    "wasi:clocks@0.2.11/monotonic-clock" &&
                method.WitImport.FunctionName == "subscribe-duration");
        Assert.Contains(result.Program.WitImportMethods,
            method => method.WitImport?.InterfaceName ==
                    "netwasm:runtime@1.0.0/reactor-host" &&
                method.WitImport.FunctionName == "watch");
        Assert.Contains(
            "cm32p2|netwasm:runtime/reactor-guest@1|wake",
            System.Text.Encoding.UTF8.GetString(result.ApplicationModule),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesAsyncCleanupAcrossPostTestLoopExits(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System.Threading.Tasks;

            namespace NestedAsyncCleanupFixture;

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    _trace = 0;
                    var operation = ExecuteAsync(input);
                    return operation.IsCompleted
                        ? operation.GetAwaiter().GetResult() * 1000 + _trace
                        : -1;
                }

                private static async Task<int> ExecuteAsync(int input)
                {
                    var remaining = input + 1;
                    do
                    {
                        try
                        {
                            _trace = _trace * 10 + 1;
                            remaining--;
                            await Task.CompletedTask.ConfigureAwait(false);
                        }
                        finally
                        {
                            _trace = _trace * 10 + 2;
                        }
                    }
                    while (remaining > 0);

                    return 5;
                }
            }
            """;
        var assembly = optimized
            ? assets.CompileOptimizedSource("NestedAsyncCleanupFixture", source)
            : assets.CompileSource("NestedAsyncCleanupFixture", source);
        var result = NetWasmCompiler.Compile(CreateAsyncOptions(
            assets,
            assembly,
            "NestedAsyncCleanupFixture.EntryPoint",
            "Run",
            target));

        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(5012, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            0,
            target,
            result.StaticDataEnd));
        Assert.Equal(6212, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            1,
            target,
            result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesSpanPrefixComparisonAfterCompletedValueTaskAwait(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;

            namespace AsyncStructReturnFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    using var stream = input == 0
                        ? new MemoryStream([0xef, 0xbb, 0xbf])
                        : new MemoryStream([(byte)input]);
                    var state = new BufferState(4);
                    state = state.ReadAsync(stream).GetAwaiter().GetResult();
                    return state.Result;
                }

                private struct BufferState
                {
                    private static readonly byte[] Prefix = [0xef, 0xbb, 0xbf];
                    private byte[] _buffer;
                    private int _count;
                    private int _maxCount;
                    private bool _isFirstBlock;

                    public BufferState(int size)
                    {
                        _buffer = new byte[size];
                        _count = 0;
                        _maxCount = 0;
                        _isFirstBlock = true;
                    }

                    public readonly async ValueTask<BufferState> ReadAsync(Stream stream)
                    {
                        var state = this;
                        var bytesRead = await stream.ReadAsync(
                            state._buffer.AsMemory(state._count),
                            CancellationToken.None).ConfigureAwait(false);
                        state._count += bytesRead;
                        state.ProcessReadBytes();
                        return state;
                    }

                    public readonly int Result =>
                        _buffer[0] * 1000 + _count * 100 + _maxCount * 10 +
                        (_isFirstBlock ? 1 : 0);

                    private void ProcessReadBytes()
                    {
                        if (_count > _maxCount)
                        {
                            _maxCount = _count;
                        }
                        if (_isFirstBlock)
                        {
                            _isFirstBlock = false;
                            if (_buffer.AsSpan(0, _count).StartsWith(Prefix))
                            {
                                _count -= Prefix.Length;
                            }
                        }
                    }
                }
            }
            """;
        var assembly = optimized
            ? assets.CompileOptimizedSource("AsyncStructReturnFixture", source)
            : assets.CompileSource("AsyncStructReturnFixture", source);
        var result = NetWasmCompiler.Compile(CreateAsyncOptions(
            assets,
            assembly,
            "AsyncStructReturnFixture.EntryPoint",
            "Run",
            target));

        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(3110, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            3,
            target,
            result.StaticDataEnd));
        Assert.Equal(239030, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            0,
            target,
            result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesStructAcrossConstrainedGenericValueTaskDispatch(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Threading.Tasks;

            namespace ConstrainedAsyncStructFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var firstSource = new FirstSource(input);
                    var first = new Reader<FirstResult>().ReadAsync(
                            firstSource,
                            new FirstBufferState(firstSource))
                        .GetAwaiter().GetResult();
                    var secondSource = new SecondSource(input + 1);
                    var second = new Reader<SecondResult>().ReadAsync(
                            secondSource,
                            new SecondBufferState(secondSource))
                        .GetAwaiter().GetResult();
                    return first.Value * 100 + second.Value;
                }

                private sealed class Reader<TResult>
                {
                    public async ValueTask<TResult?> ReadAsync<TState, TSource>(
                        TSource source,
                        TState state)
                        where TState : struct, IReadState<TState, TSource, TResult>
                    {
                        try
                        {
                            state = await state.ReadAsync(source).ConfigureAwait(false);
                            state.GetValue(out var value);
                            return value;
                        }
                        finally
                        {
                            state.Dispose();
                        }
                    }
                }

                private interface IReadState<TState, TSource, TResult> : IDisposable
                    where TState : struct, IReadState<TState, TSource, TResult>
                {
                    ValueTask<TState> ReadAsync(TSource source);
                    void GetValue(out TResult value);
                }

                private sealed class FirstSource(int value)
                {
                    public int Value { get; } = value;
                }

                private sealed class FirstResult(int value)
                {
                    public int Value { get; } = value;
                }

                private struct FirstBufferState(FirstSource source) :
                    IReadState<FirstBufferState, FirstSource, FirstResult>
                {
                    private readonly FirstSource _source = source;
                    private int _value;

                    public ValueTask<FirstBufferState> ReadAsync(FirstSource source)
                    {
                        var state = this;
                        state._value = _source.Value * 10 + source.Value;
                        return new(state);
                    }

                    public readonly void GetValue(out FirstResult value) =>
                        value = new(_value);
                    public readonly void Dispose() { }
                }

                private sealed class SecondSource(int value)
                {
                    public int Value { get; } = value;
                }

                private sealed class SecondResult(int value)
                {
                    public int Value { get; } = value;
                }

                private struct SecondBufferState(SecondSource source) :
                    IReadState<SecondBufferState, SecondSource, SecondResult>
                {
                    private readonly SecondSource _source = source;
                    private int _value;

                    public ValueTask<SecondBufferState> ReadAsync(SecondSource source)
                    {
                        var state = this;
                        state._value = _source.Value * 10 + source.Value;
                        return new(state);
                    }

                    public readonly void GetValue(out SecondResult value) =>
                        value = new(_value);
                    public readonly void Dispose() { }
                }
            }
            """;
        var assembly = optimized
            ? assets.CompileOptimizedSource("ConstrainedAsyncStructFixture", source)
            : assets.CompileSource("ConstrainedAsyncStructFixture", source);
        var result = NetWasmCompiler.Compile(CreateAsyncOptions(
            assets,
            assembly,
            "ConstrainedAsyncStructFixture.EntryPoint",
            "Run",
            target));

        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(3344, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            3,
            target,
            result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPropagatesSuspendedValueTaskFailureAndCancellationForEveryProfile(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Threading.Tasks;
            using System.Threading.Tasks.Sources;

            namespace AsyncFailureFixture;

            public sealed class Source : IValueTaskSource<int>
            {
                private ManualResetValueTaskSourceCore<int> _core;

                public ValueTask<int> Task => new(this, _core.Version);
                public void Fail() => _core.SetException(new InvalidOperationException());
                public void Cancel() => _core.SetException(new OperationCanceledException());
                public int GetResult(short token) => _core.GetResult(token);
                public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
                public void OnCompleted(
                    Action<object?> continuation,
                    object? state,
                    short token,
                    ValueTaskSourceOnCompletedFlags flags) =>
                    _core.OnCompleted(continuation, state, token, flags);
            }

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    _trace = 0;
                    var source = new Source();
                    var pending = Observe(source.Task);
                    if (pending.IsCompleted) return -1;
                    if (input == 0)
                    {
                        source.Fail();
                    }
                    else
                    {
                        source.Cancel();
                    }
                    return pending.Result * 100 + _trace;
                }

                private static async Task<int> Observe(ValueTask<int> value)
                {
                    try
                    {
                        try
                        {
                            await value.ConfigureAwait(false);
                            return -2;
                        }
                        catch (InvalidOperationException)
                        {
                            _trace = _trace * 10 + 1;
                            throw;
                        }
                        catch (OperationCanceledException)
                        {
                            _trace = _trace * 10 + 4;
                            throw;
                        }
                        finally
                        {
                            _trace = _trace * 10 + 2;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        _trace = _trace * 10 + 3;
                        return 7;
                    }
                    catch (OperationCanceledException)
                    {
                        _trace = _trace * 10 + 5;
                        return 6;
                    }
                }
            }
            """;
        var assembly = optimized
            ? assets.CompileOptimizedSource("AsyncFailureFixture", source)
            : assets.CompileSource("AsyncFailureFixture", source);
        var result = NetWasmCompiler.Compile(CreateAsyncOptions(
            assets,
            assembly,
            "AsyncFailureFixture.EntryPoint",
            "Run",
            target));

        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(823, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            0,
            target,
            result.StaticDataEnd));
        Assert.Equal(1025, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            1,
            target,
            result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerCleansUpFaultedAsyncIteratorForEveryProfile(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace AsyncIteratorFailureFixture;

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    _trace = 0;
                    return Consume(input).Result * 100 + _trace;
                }

                private static async Task<int> Consume(int input)
                {
                    try
                    {
                        await foreach (var value in Values(input))
                        {
                            _trace = _trace * 10 + value;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        _trace = _trace * 10 + 3;
                    }

                    return 7;
                }

                private static async IAsyncEnumerable<int> Values(int input)
                {
                    try
                    {
                        yield return input;
                        await Task.CompletedTask.ConfigureAwait(false);
                        throw new InvalidOperationException();
                    }
                    finally
                    {
                        _trace = _trace * 10 + 2;
                    }
                }
            }
            """;
        var assembly = optimized
            ? assets.CompileOptimizedSource("AsyncIteratorFailureFixture", source)
            : assets.CompileSource("AsyncIteratorFailureFixture", source);
        var result = NetWasmCompiler.Compile(CreateAsyncOptions(
            assets,
            assembly,
            "AsyncIteratorFailureFixture.EntryPoint",
            "Run",
            target));

        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(823, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            1,
            target,
            result.StaticDataEnd));
    }

    private static CompilerOptions CreateAsyncOptions(
        TestAssets assets,
        string assembly,
        string entryType,
        string entryMethod,
        WasmTarget target = WasmTarget.Wasm32) => new(
            assembly,
            [assets.CoreLib],
            entryType,
            entryMethod,
            [new RequestedExport("run", entryType, entryMethod)],
            WitPath: Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform@1.0.0/async-platform",
            Target: target);
}
