// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using System.Collections.Immutable;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;

namespace NetWasm.Compiler.Tests;

public sealed class DiagnosticsProfileIntegrationTests
{
    [Fact]
    public void ActivityEntropyWithoutCapabilityBoundaryFailsDeterministically()
    {
        using var assets = TestAssets.Create();
        var entryAssembly = assets.CompileSource("ActivityEntropyDenied", """
            using System.Diagnostics;

            namespace NetWasm.Diagnostics.Fixtures;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    ActivityTraceId traceId = ActivityTraceId.CreateRandom();
                    return traceId == default ? input : input + 1;
                }
            }
            """);
        var options = CompilerTestSupport.Options(
            assets,
            [],
            "NetWasm.Diagnostics.Fixtures.EntryPoint",
            "Run") with
        {
            EntryAssemblyPath = entryAssembly,
            ReferencePaths = [assets.CoreLib],
        };

        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));
        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
    }

    [Fact]
    public void RootedProfilesRetainOnlyReachableDiagnosticsFamilies()
    {
        using var assets = TestAssets.Create();
        var activity = CompileRoot(assets, "ActivityRoot", """
            using System.Diagnostics;

            namespace NetWasm.Diagnostics.Fixtures;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    ActivityTraceId traceId = ActivityTraceId.CreateFromString(
                        "0123456789abcdef0123456789abcdef");
                    return traceId == default ? input : input + 1;
                }
            }
            """);
        var metrics = CompileRoot(assets, "MetricsRoot", """
            using System.Diagnostics.Metrics;

            namespace NetWasm.Diagnostics.Fixtures;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Meter meter = new("metrics-root");
                    return meter.Name.Length + input;
                }
            }
            """);
        var none = CompileRoot(assets, "NoDiagnosticsRoot", """
            namespace NetWasm.Diagnostics.Fixtures;

            public static class EntryPoint
            {
                public static int Run(int input) => 42 + input;
            }
            """);

        var activityMethods = ReachableMethodNames(activity);
        var metricsMethods = ReachableMethodNames(metrics);
        var noneMethods = ReachableMethodNames(none);
        var noneTypes = ReachableTypeNames(none);
        var activityImports = ReachableImportNames(activity);
        var metricsImports = ReachableImportNames(metrics);
        var noneImports = ReachableImportNames(none);

        Assert.Contains(activityMethods, name => name.Contains("System.Diagnostics.ActivityTraceId", StringComparison.Ordinal));
        Assert.DoesNotContain(activityMethods, name => name.Contains("System.Diagnostics.Metrics", StringComparison.Ordinal));
        Assert.DoesNotContain(activityImports, name => name.Contains("random", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(metricsMethods, name => name.Contains("System.Diagnostics.Activity", StringComparison.Ordinal));
        Assert.Contains(metricsMethods, name => name.Contains("System.Diagnostics.Metrics.Meter", StringComparison.Ordinal));
        Assert.DoesNotContain(metricsImports, name => name.Contains("random", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(metricsImports, name => name.Contains("clock", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(noneMethods, name => name.Contains("System.Diagnostics.Activity", StringComparison.Ordinal));
        Assert.DoesNotContain(noneMethods, name => name.Contains("System.Diagnostics.Metrics", StringComparison.Ordinal));
        Assert.DoesNotContain(noneMethods, name => name.Contains("System.Diagnostics", StringComparison.Ordinal));
        Assert.DoesNotContain(noneTypes, name => name.Contains("System.Diagnostics", StringComparison.Ordinal));
        Assert.Empty(noneImports);
        Assert.Empty(none.Program.WitImportMethods);
        Assert.Empty(none.Program.JSImportMethods);
        Assert.DoesNotContain(none.Program.StringLiterals, value => value.Contains(
            "System.Diagnostics", StringComparison.Ordinal));
        Assert.DoesNotContain(none.Program.MethodInstances.Values, method => method.Definition.DeclaringType.Assembly.Name.Contains(
            "System.Diagnostics", StringComparison.Ordinal));
        Assert.DoesNotContain(SerializedArtifactTexts(none), value => value.Contains(
            "System.Diagnostics", StringComparison.Ordinal));
        Assert.DoesNotContain(Encoding.UTF8.GetString(none.ApplicationModule),
            "System.Diagnostics", StringComparison.Ordinal);

        foreach (var profile in new[] { activity, metrics, none })
        {
            var methods = ReachableMethodNames(profile);
            Assert.DoesNotContain(methods, name => name.Contains("EventSource", StringComparison.Ordinal));
            Assert.DoesNotContain(methods, name => name.Contains("EventPipe", StringComparison.Ordinal));
            Assert.DoesNotContain(methods, name => name.Contains("System.Reflection", StringComparison.Ordinal));
            Assert.DoesNotContain(methods, name => name.Contains("JavaScript", StringComparison.Ordinal));
            Assert.DoesNotContain(profile.Program.WitImportMethods,
                method => method.WitImport?.InterfaceName.Contains("javascript", StringComparison.OrdinalIgnoreCase) == true ||
                    method.WitImport?.FunctionName.Contains("javascript", StringComparison.OrdinalIgnoreCase) == true);
            Assert.DoesNotContain(Encoding.UTF8.GetString(profile.ApplicationModule),
                "EventSource", StringComparison.Ordinal);
            Assert.DoesNotContain(Encoding.UTF8.GetString(profile.ApplicationModule),
                "EventPipe", StringComparison.Ordinal);
            AssertNoGeneratedDiagnosticsMetadata(profile);
        }
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ControlledHeaderConsumerExecutesLinkedNetWasmTracingClosure(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var result = CompileControlledHeaderRoot(assets, "ControlledHeaderConsumer", """
            using System;
            using System.Collections.Generic;
            using System.Diagnostics;

            namespace NetWasm.Diagnostics.Fixtures;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var firstRoot = new Activity("first-root")
                        .SetIdFormat(ActivityIdFormat.Hierarchical)
                        .Start();
                    string firstRootId = firstRoot.Id!;
                    var child = new Activity("child").Start();
                    string childId = child.Id!;
                    child.Stop();
                    firstRoot.Stop();

                    var secondRoot = new Activity("second-root")
                        .SetIdFormat(ActivityIdFormat.Hierarchical)
                        .Start();
                    string secondRootId = secondRoot.Id!;
                    secondRoot.Stop();

                    int firstSuffix = firstRootId.IndexOf('-');
                    int secondSuffix = secondRootId.IndexOf('-');
                    bool hierarchicalRootsAreValid =
                        firstRootId.StartsWith("|", StringComparison.Ordinal) &&
                        firstRootId.EndsWith(".", StringComparison.Ordinal) &&
                        secondRootId.StartsWith("|", StringComparison.Ordinal) &&
                        secondRootId.EndsWith(".", StringComparison.Ordinal) &&
                        firstSuffix > 1 &&
                        secondSuffix > 1 &&
                        firstRootId.Substring(firstSuffix) == secondRootId.Substring(secondSuffix) &&
                        firstRootId != secondRootId &&
                        childId.StartsWith(firstRootId, StringComparison.Ordinal) &&
                        childId != firstRootId;

                    const string parentSpanId = "00f067aa0ba902b7";
                    var activity = new Activity("controlled-consumer")
                        .SetIdFormat(ActivityIdFormat.W3C)
                        .SetParentId("00-4bf92f3577b34da6a3ce929d0e0e4736-" + parentSpanId + "-01");
                    activity.Start();
                    var propagator = DistributedContextPropagator.CreateW3CPropagator();
                    var carrier = new HeaderCarrier
                    {
                        TraceState = "vendor=value",
                    };
                    propagator.Inject(activity, carrier, SetHeader);
                    propagator.ExtractTraceIdAndState(carrier, GetHeader, out var traceParent, out var traceState);
                    activity.Stop();
                    var propagatedSpanId = traceParent is not null && traceParent.Length >= 52
                        ? traceParent.Substring(36, 16)
                        : null;
                    var negativeCasesReject =
                        !IsDistinctNonZeroLowerHex("0000000000000000", parentSpanId) &&
                        !IsDistinctNonZeroLowerHex(parentSpanId, parentSpanId) &&
                        !IsDistinctNonZeroLowerHex("not-a-span-id", parentSpanId);
                    var valid = hierarchicalRootsAreValid &&
                        traceParent is not null &&
                        traceParent.Length == 55 &&
                        traceParent.StartsWith("00-4bf92f3577b34da6a3ce929d0e0e4736-", StringComparison.Ordinal) &&
                        traceParent.EndsWith("-01", StringComparison.Ordinal) &&
                        traceState == "vendor=value" &&
                        IsDistinctNonZeroLowerHex(propagatedSpanId, parentSpanId) &&
                        negativeCasesReject;
                    return valid ? input + 100 : input - 100;
                }

                private static bool IsDistinctNonZeroLowerHex(string? spanId, string parentSpanId)
                {
                    if (spanId is null || spanId.Length != 16 ||
                        spanId == "0000000000000000" || spanId == parentSpanId)
                    {
                        return false;
                    }

                    for (var index = 0; index < spanId.Length; index++)
                    {
                        var character = spanId[index];
                        if (!((character >= '0' && character <= '9') ||
                            (character >= 'a' && character <= 'f')))
                        {
                            return false;
                        }
                    }

                    return true;
                }

                private static void SetHeader(object? carrier, string name, string value)
                {
                    var headers = (HeaderCarrier)carrier!;
                    if (name == "traceparent")
                    {
                        headers.TraceParent = value;
                    }
                    else if (name == "tracestate")
                    {
                        headers.TraceState = value;
                    }
                }

                private static void GetHeader(
                    object? carrier,
                    string name,
                    out string? value,
                    out IEnumerable<string>? values)
                {
                    var headers = (HeaderCarrier)carrier!;
                    value = name == "traceparent" ? headers.TraceParent :
                        name == "tracestate" ? headers.TraceState : null;
                    values = null;
                }

                private sealed class HeaderCarrier
                {
                    public string? TraceParent;
                    public string? TraceState;
                }
            }
            """, optimize, target);

        var diagnostics = result.Program.Methods.Values
            .Concat(result.Program.ConstructedMethods.Values)
            .Select(method => method.Method)
            .Where(instance => instance.DeclaringType.CanonicalName.Contains(
                "System.Diagnostics", StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(diagnostics, instance => instance.DeclaringType.CanonicalName.Contains(
            "System.Diagnostics.Activity", StringComparison.Ordinal));
        Assert.Contains(diagnostics, instance => instance.DeclaringType.CanonicalName.Contains(
            "System.Diagnostics.DistributedContextPropagator", StringComparison.Ordinal));
        Assert.Contains(diagnostics, instance => instance.DeclaringType.CanonicalName.Contains(
            "System.Diagnostics.W3CPropagator", StringComparison.Ordinal));
        var coreLibAssembly = Path.GetFileNameWithoutExtension(assets.CoreLib);
        Assert.All(diagnostics, instance => Assert.Equal(
            coreLibAssembly,
            instance.Definition.DeclaringType.Assembly.Name));
        Assert.DoesNotContain(diagnostics, instance => instance.Definition.DeclaringType.Assembly.Name ==
            "System.Diagnostics.DiagnosticSource");
        Assert.Contains(
            ReachableImportNames(result),
            import => import.Contains("wasi:random", StringComparison.Ordinal));
        Assert.Empty(result.Program.JSImportMethods);
        Assert.DoesNotContain(ReachableImportNames(result), import =>
            import.Contains("http", StringComparison.OrdinalIgnoreCase) ||
            import.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            import.Contains("JSImport", StringComparison.Ordinal) ||
            import.Contains("JSExport", StringComparison.Ordinal));
        var moduleText = Encoding.UTF8.GetString(result.ApplicationModule);
        Assert.DoesNotContain(moduleText, "System.Diagnostics.DiagnosticSource", StringComparison.Ordinal);
        AssertNoGeneratedDiagnosticsMetadata(result);
        Assert.Equal(107, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            7,
            target,
            result.StaticDataEnd));
    }

    // Desktop .NET establishes each probe's result in DiagnosticsSemanticsProbes.
    [Fact]
    public void DesktopEstablishesTheDiagnosticsSemanticsResults()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("DiagnosticsSemantics.Desktop",
            [CSharpSyntaxTree.ParseText(DiagnosticsSemanticsSource, cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("DiagnosticsSemantics.Desktop", isCollectible: true);
        try
        {
            var run = context.LoadFromStream(stream).GetType("NetWasm.Diagnostics.Fixtures.EntryPoint")!.GetMethod("Run")!;
            foreach (var (probe, desktop, _) in DiagnosticsSemanticsProbes)
            {
                Assert.Equal((probe, desktop), (probe, (int)run.Invoke(null, [probe])!));
            }
        }
        finally
        {
            context.Unload();
        }
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ListenersCurrentActivityAndMetricsRunAsDesktopDoes(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var result = CompileControlledHeaderRoot(
            assets, "DiagnosticsSemantics", DiagnosticsSemanticsSource, optimize, target);

        foreach (var (probe, _, netWasm) in DiagnosticsSemanticsProbes)
        {
            Assert.Equal((probe, netWasm), (probe, CompilerTestSupport.ExecuteWithStandardWasiNode(
                result.ApplicationModule, assets.Directory, probe, target, result.StaticDataEnd)));
        }
    }

    // Each probe's input with its desktop and NetWasm results, which match except where noted.
    private static readonly (int Probe, int Desktop, int NetWasm)[] DiagnosticsSemanticsProbes =
    [
        (0, 1, 1),
        (1, 1, 1),
        (2, 1, 1),
        (3, 0x1FF, 0x1FF),
        (4, (13 << 8) | 13, (13 << 8) | 13),
        // The portable profile rejects Activity.AddException before it records an event (bits:
        // rejected, still current, no event), where desktop .NET records the exception event.
        (5, 0b010, 0b111),
    ];

    private const string DiagnosticsSemanticsSource = """
        using System;
        using System.Collections.Generic;
        using System.Diagnostics;
        using System.Diagnostics.Metrics;
        using System.Threading;
        using System.Threading.Tasks;

        namespace NetWasm.Diagnostics.Fixtures;

        public static class EntryPoint
        {
            private static readonly AsyncLocal<string?> Marker = new();

            public static int Run(int input) => input switch
            {
                0 => ActivitySourceCallbacks(),
                1 => DiagnosticListenerFiltering(),
                2 => CurrentAcrossAwait(),
                3 => PublicationOrder(),
                4 => MetricInstrumentShapes(),
                5 => AddExceptionBoundary(),
                _ => throw new ArgumentOutOfRangeException(nameof(input)),
            };

            // An ActivityListener sees its source's activity start and stop, in that order.
            private static int ActivitySourceCallbacks()
            {
                var callbacks = new List<string>();
                var listener = new ActivityListener
                {
                    ShouldListenTo = source => source.Name == "activity-probe",
                    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                    ActivityStarted = activity => callbacks.Add("start:" + activity.OperationName),
                    ActivityStopped = activity => callbacks.Add("stop:" + activity.OperationName),
                };
                ActivitySource.AddActivityListener(listener);
                try
                {
                    using var source = new ActivitySource("activity-probe");
                    using var activity = source.StartActivity("root");
                }
                finally
                {
                    listener.Dispose();
                }

                return callbacks.Count == 2 && callbacks[0] == "start:root" && callbacks[1] == "stop:root" ? 1 : 0;
            }

            // A DiagnosticListener subscription receives only the events its predicate enables.
            private static int DiagnosticListenerFiltering()
            {
                var callbacks = new List<string>();
                using var listener = new DiagnosticListener("diagnostic-probe");
                using var subscription = listener.Subscribe(new RecordingObserver(callbacks), name => name == "probe.event");
                if (listener.IsEnabled("probe.event"))
                {
                    listener.Write("probe.event", new object());
                }
                if (listener.IsEnabled("probe.ignored"))
                {
                    listener.Write("probe.ignored", new object());
                }

                return callbacks.Count == 1 && callbacks[0] == "probe.event" ? 1 : 0;
            }

            // Activity.Current and an AsyncLocal value flow through an async method and are restored
            // after it, and its changes do not leak into its caller. The awaited task is complete
            // because a synchronous entry cannot block on an incomplete one; Activity.Current is an
            // AsyncLocal, whose flow across a suspension the asynchronous entry point tests cover.
            private static int CurrentAcrossAwait()
            {
                var previous = Activity.Current;
                var previousMarker = Marker.Value;
                Marker.Value = "outer";
                var flowed = CurrentAcrossAwaitAsync().GetAwaiter().GetResult();
                var callerKept = Marker.Value == "outer";
                Marker.Value = previousMarker;
                return flowed && callerKept && ReferenceEquals(previous, Activity.Current) ? 1 : 0;
            }

            private static async Task<bool> CurrentAcrossAwaitAsync()
            {
                var previous = Activity.Current;
                using var activity = new Activity("current-probe").Start();
                Marker.Value = "nested";
                await Task.CompletedTask;
                var flowed = ReferenceEquals(activity, Activity.Current) && Marker.Value == "nested";
                activity.Stop();
                return flowed && ReferenceEquals(previous, Activity.Current);
            }

            // An activity's start, a diagnostic event written inside it and its stop publish in that
            // order, and Activity.Current is restored afterwards. Each bit is one condition.
            private static int PublicationOrder()
            {
                var activityCallbacks = new List<string>();
                var order = new List<string>();
                var activityListener = new ActivityListener
                {
                    ShouldListenTo = source => source.Name == "order-probe",
                    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                    ActivityStarted = activity =>
                    {
                        activityCallbacks.Add("start:" + activity.OperationName);
                        order.Add("activity-start");
                    },
                    ActivityStopped = activity =>
                    {
                        activityCallbacks.Add("stop:" + activity.OperationName);
                        order.Add("activity-stop");
                    },
                };
                ActivitySource.AddActivityListener(activityListener);
                var diagnosticCallbacks = new List<string>();
                var diagnosticListener = new DiagnosticListener("order-events");
                var subscription = diagnosticListener.Subscribe(
                    new RecordingObserver(diagnosticCallbacks, order), name => name == "probe.event");
                var previous = Activity.Current;
                var currentPublished = false;
                var enabled = false;
                try
                {
                    using var source = new ActivitySource("order-probe");
                    using var activity = source.StartActivity("root");
                    currentPublished = activity is not null && ReferenceEquals(activity, Activity.Current);
                    enabled = diagnosticListener.IsEnabled("probe.event");
                    if (enabled)
                    {
                        diagnosticListener.Write("probe.event", new object());
                    }
                    if (diagnosticListener.IsEnabled("probe.ignored"))
                    {
                        diagnosticListener.Write("probe.ignored", new object());
                    }
                }
                finally
                {
                    subscription.Dispose();
                    diagnosticListener.Dispose();
                    activityListener.Dispose();
                }

                var observation = 0;
                observation |= currentPublished ? 1 << 0 : 0;
                observation |= enabled ? 1 << 1 : 0;
                observation |= diagnosticCallbacks.Count == 1 ? 1 << 2 : 0;
                observation |= activityCallbacks.Count == 2 ? 1 << 3 : 0;
                observation |= activityCallbacks.Count >= 1 && activityCallbacks[0] == "start:root" ? 1 << 4 : 0;
                observation |= activityCallbacks.Count >= 2 && activityCallbacks[1] == "stop:root" ? 1 << 5 : 0;
                observation |= diagnosticCallbacks.Count >= 1 && diagnosticCallbacks[0] == "probe.event" ? 1 << 6 : 0;
                observation |= ReferenceEquals(previous, Activity.Current) ? 1 << 7 : 0;
                observation |= order.Count == 3 && order[0] == "activity-start" &&
                    order[1] == "diagnostic:probe.event" && order[2] == "activity-stop" ? 1 << 8 : 0;
                return observation;
            }

            // Every synchronous and observable instrument publishes once and measures once:
            // published count in the second byte, measurement count in the first. The listener
            // takes only this meter's instruments, as a process can hold others (desktop's runtime
            // meters, for one).
            private static int MetricInstrumentShapes()
            {
                using var meter = new Meter("metrics-probe");
                using var listener = new MeterListener();
                var published = 0;
                var measurements = 0;
                listener.InstrumentPublished = (instrument, current) =>
                {
                    if (instrument.Meter != meter)
                    {
                        return;
                    }
                    published++;
                    current.EnableMeasurementEvents(instrument);
                };
                listener.SetMeasurementEventCallback<int>((_, _, _, _) => measurements++);
                listener.Start();

                meter.CreateCounter<int>("counter").Add(1);
                meter.CreateGauge<int>("gauge").Record(2);
                meter.CreateUpDownCounter<int>("updown").Add(3);
                meter.CreateHistogram<int>("histogram").Record(4);
                meter.CreateObservableCounter<int>("counter-value", () => 5);
                meter.CreateObservableCounter<int>("counter-measurement", () => new Measurement<int>(6));
                meter.CreateObservableCounter<int>("counter-sequence", () => new[] { new Measurement<int>(7) });
                meter.CreateObservableGauge<int>("gauge-value", () => 8);
                meter.CreateObservableGauge<int>("gauge-measurement", () => new Measurement<int>(9));
                meter.CreateObservableGauge<int>("gauge-sequence", () => new[] { new Measurement<int>(10) });
                meter.CreateObservableUpDownCounter<int>("updown-value", () => 11);
                meter.CreateObservableUpDownCounter<int>("updown-measurement", () => new Measurement<int>(12));
                meter.CreateObservableUpDownCounter<int>("updown-sequence", () => new[] { new Measurement<int>(13) });
                listener.RecordObservableInstruments();
                return (published << 8) | measurements;
            }

            // Bits: AddException threw PlatformNotSupportedException, the activity stayed current,
            // and no event was recorded.
            private static int AddExceptionBoundary()
            {
                using var activity = new Activity("exception-boundary").Start();
                var rejected = false;
                try
                {
                    activity.AddException(new InvalidOperationException("boundary"));
                }
                catch (PlatformNotSupportedException)
                {
                    rejected = true;
                }

                using var events = activity.Events.GetEnumerator();
                return (rejected ? 1 << 0 : 0) |
                    (ReferenceEquals(Activity.Current, activity) ? 1 << 1 : 0) |
                    (!events.MoveNext() ? 1 << 2 : 0);
            }

            private sealed class RecordingObserver(List<string> callbacks, List<string>? order = null)
                : IObserver<KeyValuePair<string, object?>>
            {
                public void OnCompleted()
                {
                }

                public void OnError(Exception error)
                {
                }

                public void OnNext(KeyValuePair<string, object?> value)
                {
                    callbacks.Add(value.Key);
                    order?.Add("diagnostic:" + value.Key);
                }
            }
        }
        """;

    private static CompilationResult CompileRoot(TestAssets assets, string assemblyName, string source)
    {
        Assert.True(
            Regex.IsMatch(
                source,
                @"public\s+static\s+int\s+Run\s*\(\s*int(?:\s+\w+)?\s*\)",
                RegexOptions.CultureInvariant),
            $"fixture '{assemblyName}' must expose the established static int Run(int) entry contract");
        var entryAssembly = assets.CompileSource(assemblyName, source);
        var entryType = "NetWasm.Diagnostics.Fixtures.EntryPoint";
        var options = CompilerTestSupport.Options(assets, [], entryType, "Run") with
        {
            EntryAssemblyPath = entryAssembly,
            ReferencePaths = [assets.CoreLib],
            // As the SDK compiles every program (EventSourceSupport=false): CoreLib's own event logging, such as
            // ArrayPool's, leaves the program through its ILLink.Substitutions stubs.
            FeatureSwitches = ImmutableDictionary<string, bool>.Empty
                .Add("System.Diagnostics.Tracing.EventSource.IsSupported", false),
        };
        return NetWasmCompiler.Compile(options);
    }

    private static CompilationResult CompileControlledHeaderRoot(
        TestAssets assets,
        string assemblyName,
        string source,
        bool optimize = false,
        WasmTarget target = WasmTarget.Wasm32)
    {
        Assert.True(
            Regex.IsMatch(
                source,
                @"public\s+static\s+int\s+Run\s*\(\s*int(?:\s+\w+)?\s*\)",
                RegexOptions.CultureInvariant),
            $"fixture '{assemblyName}' must expose the established static int Run(int) entry contract");
        var entryAssembly = optimize
            ? assets.CompileOptimizedSource(assemblyName, source)
            : assets.CompileSource(assemblyName, source);
        var entryType = "NetWasm.Diagnostics.Fixtures.EntryPoint";
        var options = CompilerTestSupport.CreateReactorOptions(
            assets,
            entryAssembly,
            entryType,
            "Run") with
        {
            EntryAssemblyPath = entryAssembly,
            ReferencePaths = [assets.CoreLib],
            Target = target,
            WitWorld = "netwasm:platform/platform@1.0.0",
        };
        return NetWasmCompiler.Compile(options);
    }

    private static HashSet<string> ReachableMethodNames(CompilationResult result) =>
        result.Program.MethodInstances.Values.Select(method => method.CanonicalName)
            .Concat(result.Program.ConstructedMethods.Keys)
            .Concat(result.Program.Methods.Values
                .Select(method => method.Method.CanonicalName))
            .Concat(result.Program.ConstructedMethods.Values
                .Select(method => method.Method.CanonicalName))
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> ReachableTypeNames(CompilationResult result) =>
        result.Program.ConstructedTypes.Select(type => type.CanonicalName)
            .Concat(result.Program.MethodInstances.Values.Select(method => method.DeclaringType.CanonicalName))
            .Concat(result.Program.Methods.Values
                .Select(method => method.Method.DeclaringType.CanonicalName))
            .Concat(result.Program.ConstructedMethods.Values
                .Select(method => method.Method.DeclaringType.CanonicalName))
            .ToHashSet(StringComparer.Ordinal);

    private static void AssertNoGeneratedDiagnosticsMetadata(CompilationResult result)
    {
        var forbiddenMetadataTokens = new[]
        {
            "DiagnosticTypeNameTable",
            "GeneratedDiagnosticsMetadata",
            "System.Diagnostics.DiagnosticSource",
            "Type.FullName",
            "Type.Name",
            "Type.Namespace",
        };
        foreach (var token in forbiddenMetadataTokens)
        {
            Assert.DoesNotContain(SerializedArtifactTexts(result),
                value => value.Contains(token, StringComparison.Ordinal));
        }
    }

    private static IEnumerable<string> SerializedArtifactTexts(CompilationResult result)
    {
        yield return Encoding.UTF8.GetString(result.ApplicationModule);
        foreach (var literal in result.Program.StringLiterals)
        {
            yield return literal;
        }

        if (result.TypeSymbols is { } types)
        {
            yield return Encoding.Unicode.GetString(types.Bytes);
        }
    }

    private static HashSet<string> ReachableImportNames(CompilationResult result) =>
        result.Program.WitImportMethods
            .Select(method => $"{method.WitImport?.InterfaceName}/{method.WitImport?.FunctionName}")
            .Concat(result.Program.JSImportMethods.Select(method => $"{method.JSImport?.ModuleName}/{method.JSImport?.FunctionName}"))
            .ToHashSet(StringComparer.Ordinal);
}
