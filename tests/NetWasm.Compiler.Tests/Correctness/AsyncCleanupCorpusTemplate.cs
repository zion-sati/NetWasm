using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class AsyncCleanupCorpusTemplate : IGeneratedCorpusTemplate
{
    private static readonly ImmutableDictionary<string, SuspensionShape> Suspensions =
        new Dictionary<string, SuspensionShape>(StringComparer.Ordinal)
        {
            ["completed"] = new(
                "return Execute(input).Result;",
                "await Task.CompletedTask; total += 3;"),
            ["value-task"] = new(
                "return Execute(input).Result;",
                "total += await Immediate(input);"),
            ["pending"] = new(
                "_pending = new TaskCompletionSource<int>(); var execution = Execute(input); _pending.SetResult(input + 7); return execution.Result;",
                "total += await _pending!.Task;"),
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, string> Cleanup =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["finally"] =
                "try { __WORK__ } finally { _trace = unchecked(_trace * 17 + 101); }",
            ["async-dispose"] =
                "await using (var resource = new Resource()) { __WORK__ }",
            ["iterator-dispose"] =
                "__WORK__ await foreach (var item in Values(input)) { total += item; if (item == input) break; }",
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, string> Outcomes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["success"] = "total += 11;",
            ["failure"] =
                "try { total += await Failed(); } catch (InvalidOperationException) { _trace += 107; total += 13; }",
            ["cancellation"] =
                "try { total += await Canceled(); } catch (OperationCanceledException) { _trace += 109; total += 17; }",
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, GcShape> Collections =
        // These call placements exercise lowering and async control flow. The
        // simulated oracle cannot qualify survival across real collections.
        new Dictionary<string, GcShape>(StringComparer.Ordinal)
        {
            ["none"] = new("", ""),
            ["before-suspension"] = new("GC.Collect(); ", ""),
            ["after-suspension"] = new("", " GC.Collect();"),
        }.ToImmutableDictionary(StringComparer.Ordinal);

    public string Family => "AsyncCleanup";

    public int Seed => 0x55667711;

    public ImmutableArray<CorpusDimension> Dimensions =>
    [
        new("suspension", ["completed", "value-task", "pending"]),
        new("cleanup", ["finally", "async-dispose", "iterator-dispose"]),
        new("outcome", ["success", "failure", "cancellation"]),
        new("gc", ["none", "before-suspension", "after-suspension"]),
    ];

    public ImmutableArray<ImmutableDictionary<string, string>> TargetedCases
    {
        get
        {
            var cases = ImmutableArray.CreateBuilder<
                ImmutableDictionary<string, string>>();
            foreach (var suspension in Suspensions.Keys.Order(StringComparer.Ordinal))
                foreach (var cleanup in Cleanup.Keys.Order(StringComparer.Ordinal))
                    foreach (var gc in Collections.Keys.Order(StringComparer.Ordinal))
                    {
                        cases.Add(Values(
                            ("suspension", suspension),
                            ("cleanup", cleanup),
                            ("outcome", "success"),
                            ("gc", gc)));
                    }
            return cases.ToImmutable();
        }
    }

    public CorpusFixture Create(
        string caseName,
        ImmutableDictionary<string, string> values)
    {
        var suspension = Suspensions[values["suspension"]];
        var collection = Collections[values["gc"]];
        var work = collection.Before + suspension.Await + ' ' +
            Outcomes[values["outcome"]] + collection.After;
        var cleanup = Cleanup[values["cleanup"]]
            .Replace("__WORK__", work, StringComparison.Ordinal);
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace NetWasm.Correctness.Generated.{{caseName}};

            public sealed class Resource : IAsyncDisposable
            {
                public ValueTask DisposeAsync()
                {
                    EntryPoint.AddTrace(103);
                    return default;
                }
            }

            public static class EntryPoint
            {
                private static int _trace;
                private static TaskCompletionSource<int>? _pending;

                public static int Run(int input)
                {
                    _trace = 1;
                    _pending = null;
                    _trace += _pending is null ? 0 : 1;
                    {{suspension.Start}}
                }

                public static int Trace() => _trace;

                public static void AddTrace(int value) => _trace += value;

                private static async Task<int> Execute(int input)
                {
                    var total = input;
                    {{cleanup}}
                    _trace = unchecked(_trace * 31 + total);
                    return total;
                }

                private static async ValueTask<int> Immediate(int value)
                {
                    await Task.CompletedTask;
                    return value + 5;
                }

                private static Task<int> Failed()
                {
                    var source = new TaskCompletionSource<int>();
                    source.SetException(new InvalidOperationException());
                    return source.Task;
                }

                private static Task<int> Canceled()
                {
                    var source = new TaskCompletionSource<int>();
                    source.SetCanceled();
                    return source.Task;
                }

                private static async IAsyncEnumerable<int> Values(int input)
                {
                    yield return input;
                    await Task.CompletedTask;
                    yield return input + 1;
                }
            }
            """;
        return new(caseName, $"NetWasm.Correctness.Generated.{caseName}", source,
            [-1, 0, 2])
        {
            RequiresReactor = true,
        };
    }

    private static ImmutableDictionary<string, string> Values(
        params (string Name, string Value)[] values) => values
        .ToImmutableDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);

    private sealed record SuspensionShape(string Start, string Await);

    private sealed record GcShape(string Before, string After);
}
