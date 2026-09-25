using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices.WebAssembly;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Diagnostics.Components;

public static class DiagnosticsComponent
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string ParentSpanId = "00f067aa0ba902b7";
    private const ulong ValidObservation = 0x0123_4567_89ab_cdef;
    private static readonly AsyncLocal<string?> CurrentMarker = new();

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "sample")]
    public static ulong Sample()
    {
        var activity = new Activity("component-consumer")
            .SetIdFormat(ActivityIdFormat.W3C)
            .SetParentId($"00-{TraceId}-{ParentSpanId}-01");
        activity.Start();

        var propagator = DistributedContextPropagator.CreateW3CPropagator();
        var carrier = new HeaderCarrier { TraceState = "vendor=value" };
        propagator.Inject(activity, carrier, SetHeader);
        propagator.ExtractTraceIdAndState(carrier, GetHeader, out var traceParent, out var traceState);
        activity.Stop();

        var propagatedSpanId = traceParent is not null && traceParent.Length >= 52
            ? traceParent.Substring(36, 16)
            : null;
        var negativeCasesReject =
            !IsDistinctNonZeroLowerHex("0000000000000000", ParentSpanId) &&
            !IsDistinctNonZeroLowerHex(ParentSpanId, ParentSpanId) &&
            !IsDistinctNonZeroLowerHex("not-a-span-id", ParentSpanId);
        var valid = traceParent is not null &&
            traceParent.Length == 55 &&
            traceParent.StartsWith($"00-{TraceId}-", StringComparison.Ordinal) &&
            traceParent.EndsWith("-01", StringComparison.Ordinal) &&
            traceState == "vendor=value" &&
            IsDistinctNonZeroLowerHex(propagatedSpanId, ParentSpanId) &&
            negativeCasesReject;

        return valid ? ValidObservation : 0;
    }

    public static int Run(int input) => checked(input + (Sample() == ValidObservation ? 1 : -1));

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "trace-activity")]
    public static ulong TraceActivity()
    {
        const string sourceName = "component-activity-probe";
        var callbacks = new List<string>();
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => callbacks.Add("start:" + activity.OperationName),
            ActivityStopped = activity => callbacks.Add("stop:" + activity.OperationName),
        };
        ActivitySource.AddActivityListener(listener);
        try
        {
            using var source = new ActivitySource(sourceName);
            using var activity = source.StartActivity("root");
        }
        finally
        {
            listener.Dispose();
        }

        return callbacks.Count == 2 && callbacks[0] == "start:root" &&
            callbacks[1] == "stop:root" ? 1UL : 0UL;
    }

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "trace-diagnostic")]
    public static ulong TraceDiagnostic()
    {
        var callbacks = new List<string>();
        using var listener = new DiagnosticListener("component-diagnostic-probe");
        using var subscription = listener.Subscribe(
            new RecordingObserver(callbacks),
            name => name == "component.event");
        if (listener.IsEnabled("component.event"))
        {
            listener.Write("component.event", new object());
        }
        if (listener.IsEnabled("component.ignored"))
        {
            listener.Write("component.ignored", new object());
        }

        return callbacks.Count == 1 && callbacks[0] == "component.event" ? 1UL : 0UL;
    }

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "trace-current")]
    public static ulong TraceCurrent()
    {
        var previous = Activity.Current;
        var previousMarker = CurrentMarker.Value;
        CurrentMarker.Value = "outer";
        var flowedAndRestored = TraceCurrentAsync().GetAwaiter().GetResult();
        CurrentMarker.Value = previousMarker;
        return flowedAndRestored && ReferenceEquals(previous, Activity.Current) &&
            CurrentMarker.Value == previousMarker ? 1UL : 0UL;
    }

    private static async ValueTask<bool> TraceCurrentAsync()
    {
        var previous = Activity.Current;
        var previousMarker = CurrentMarker.Value;
        using var activity = new Activity("component-current-probe").Start();
        CurrentMarker.Value = "nested";
        await ValueTask.CompletedTask;
        var flowed = ReferenceEquals(activity, Activity.Current) && CurrentMarker.Value == "nested";
        activity.Stop();
        CurrentMarker.Value = previousMarker;
        return flowed && ReferenceEquals(previous, Activity.Current) &&
            CurrentMarker.Value == previousMarker;
    }

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "trace")]
    public static ulong Trace()
    {
        var stage = 0;
        try
        {
            return TraceCore(ref stage);
        }
        catch (Exception)
        {
            return (1UL << 63) | ((ulong)(uint)stage << 48);
        }
    }

    private static ulong TraceCore(ref int stage)
    {
        stage = 1;
        const string sourceName = "component-source";
        const string diagnosticName = "component-events";
        var activityCallbacks = new List<string>();
        var publicationOrder = new List<string>();
        var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                activityCallbacks.Add("start:" + activity.OperationName);
                publicationOrder.Add("activity-start:" + activity.OperationName);
            },
            ActivityStopped = activity =>
            {
                activityCallbacks.Add("stop:" + activity.OperationName);
                publicationOrder.Add("activity-stop:" + activity.OperationName);
            },
        };
        stage = 2;
        ActivitySource.AddActivityListener(activityListener);

        stage = 3;
        var diagnosticCallbacks = new List<string>();
        var diagnosticListener = new DiagnosticListener(diagnosticName);
        var subscription = diagnosticListener.Subscribe(
            new RecordingObserver(diagnosticCallbacks, publicationOrder),
            name => name == "component.event");
        var previous = Activity.Current;
        var currentPublished = false;
        var enabled = false;
        try
        {
            stage = 4;
            using var source = new ActivitySource(sourceName);
            using var activity = source.StartActivity("root");
            currentPublished = activity is not null && ReferenceEquals(activity, Activity.Current);
            enabled = diagnosticListener.IsEnabled("component.event");
            if (enabled)
            {
                diagnosticListener.Write("component.event", new object());
            }
            if (diagnosticListener.IsEnabled("component.ignored"))
            {
                diagnosticListener.Write("component.ignored", new object());
            }
            stage = 5;
        }
        finally
        {
            stage = 6;
            subscription.Dispose();
            diagnosticListener.Dispose();
            activityListener.Dispose();
            if (!ReferenceEquals(previous, Activity.Current))
            {
                Activity.Current = previous;
            }
        }

        stage = 7;
        var currentRestored = ReferenceEquals(previous, Activity.Current);
        var observation = 0UL;
        observation |= currentPublished ? 1UL << 0 : 0;
        observation |= enabled ? 1UL << 1 : 0;
        observation |= diagnosticCallbacks.Count == 1 ? 1UL << 2 : 0;
        observation |= activityCallbacks.Count == 2 ? 1UL << 3 : 0;
        observation |= activityCallbacks.Count >= 1 &&
            activityCallbacks[0] == "start:root" ? 1UL << 4 : 0;
        observation |= activityCallbacks.Count >= 2 &&
            activityCallbacks[1] == "stop:root" ? 1UL << 5 : 0;
        observation |= diagnosticCallbacks.Count >= 1 &&
            diagnosticCallbacks[0] == "component.event" ? 1UL << 6 : 0;
        observation |= currentRestored ? 1UL << 7 : 0;
        observation |= (ulong)(byte)diagnosticCallbacks.Count << 8;
        observation |= (ulong)(byte)activityCallbacks.Count << 16;
        observation |= publicationOrder.Count == 3 &&
            publicationOrder[0] == "activity-start:root" &&
            publicationOrder[1] == "diagnostic:component.event" &&
            publicationOrder[2] == "activity-stop:root" ? 1UL << 24 : 0;
        return observation;
    }

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "qualification-contract")]
    public static ulong QualificationContract()
    {
        var contract = 0UL;
        if (Sample() == ValidObservation)
        {
            contract |= 1UL << 0;
        }

        if ((Trace() & (1UL << 24)) != 0)
        {
            contract |= 1UL << 1;
        }

        if (TraceCurrent() == 1)
        {
            contract |= 1UL << 2;
        }

        if (Metrics() == ((13UL << 32) | 13UL))
        {
            contract |= 1UL << 3;
        }

        if (Boundary() == 0x07)
        {
            contract |= 1UL << 4;
        }

        if (TraceDiagnostic() == 1)
        {
            contract |= 1UL << 5;
        }

        return contract;
    }

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "metrics")]
    public static ulong Metrics()
    {
        using var meter = new Meter("component-meter");
        using var listener = new MeterListener();
        var published = 0;
        var measurements = 0;
        listener.InstrumentPublished = (instrument, current) =>
        {
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
        listener.Dispose();
        return ((ulong)(uint)published << 32) | (uint)measurements;
    }

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "metrics-synchronous")]
    public static ulong MetricsSynchronous()
    {
        var stage = 0;
        try
        {
            using var meter = new Meter("component-synchronous-combined");
            using var listener = new MeterListener();
            var published = 0;
            var measurements = 0;
            listener.InstrumentPublished = (instrument, current) =>
            {
                published++;
                current.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<int>((_, _, _, _) => measurements++);
            stage = 1;
            listener.Start();
            stage = 2;
            meter.CreateCounter<int>("counter").Add(1);
            stage = 3;
            var gauge = meter.CreateGauge<int>("gauge");
            stage = 4;
            gauge.Record(2);
            stage = 5;
            meter.CreateUpDownCounter<int>("updown").Add(3);
            stage = 6;
            meter.CreateHistogram<int>("histogram").Record(4);
            stage = 7;
            return ((ulong)(uint)published << 32) | (uint)measurements;
        }
        catch (InvalidOperationException)
        {
            return (1UL << 63) | (1UL << 40) | ((ulong)(uint)stage << 48);
        }
        catch (InvalidCastException)
        {
            return (1UL << 63) | (2UL << 40) | ((ulong)(uint)stage << 48);
        }
        catch (ArgumentException)
        {
            return (1UL << 63) | (3UL << 40) | ((ulong)(uint)stage << 48);
        }
        catch (Exception)
        {
            return (1UL << 63) | (4UL << 40) | ((ulong)(uint)stage << 48);
        }
    }

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "metrics-listener")]
    public static ulong MetricsListener() => MetricsSynchronousProbe(-1);

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "metrics-counter")]
    public static ulong MetricsCounter() => MetricsSynchronousProbe(0);

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "metrics-gauge")]
    public static ulong MetricsGauge() => MetricsSynchronousProbe(1);

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "metrics-up-down")]
    public static ulong MetricsUpDown() => MetricsSynchronousProbe(2);

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "metrics-histogram")]
    public static ulong MetricsHistogram() => MetricsSynchronousProbe(3);

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "metrics-observable-value")]
    public static ulong MetricsObservableValue() => MetricsProbe(1);

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "metrics-observable-measurement")]
    public static ulong MetricsObservableMeasurement() => MetricsProbe(2);

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "metrics-observable-sequence")]
    public static ulong MetricsObservableSequence() => MetricsProbe(3);

    private static ulong MetricsSynchronousProbe(int kind)
    {
        using var meter = new Meter("component-synchronous-probe");
        using var listener = new MeterListener();
        var published = 0;
        var measurements = 0;
        listener.InstrumentPublished = (instrument, current) =>
        {
            published++;
            current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((_, _, _, _) => measurements++);
        listener.Start();

        switch (kind)
        {
            case -1:
                break;
            case 0:
                meter.CreateCounter<int>("counter").Add(1);
                break;
            case 1:
                meter.CreateGauge<int>("gauge").Record(2);
                break;
            case 2:
                meter.CreateUpDownCounter<int>("updown").Add(3);
                break;
            case 3:
                meter.CreateHistogram<int>("histogram").Record(4);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return ((ulong)(uint)published << 32) | (uint)measurements;
    }

    private static ulong MetricsProbe(int shape)
    {
        using var meter = new Meter("component-meter-probe");
        using var listener = new MeterListener();
        var published = 0;
        var measurements = 0;
        listener.InstrumentPublished = (instrument, current) =>
        {
            published++;
            current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((_, _, _, _) => measurements++);
        listener.Start();

        switch (shape)
        {
            case 0:
                meter.CreateCounter<int>("counter").Add(1);
                meter.CreateGauge<int>("gauge").Record(2);
                meter.CreateUpDownCounter<int>("updown").Add(3);
                meter.CreateHistogram<int>("histogram").Record(4);
                break;
            case 1:
                meter.CreateObservableCounter<int>("counter", () => 5);
                meter.CreateObservableGauge<int>("gauge", () => 8);
                meter.CreateObservableUpDownCounter<int>("updown", () => 11);
                listener.RecordObservableInstruments();
                break;
            case 2:
                meter.CreateObservableCounter<int>("counter", () => new Measurement<int>(6));
                meter.CreateObservableGauge<int>("gauge", () => new Measurement<int>(9));
                meter.CreateObservableUpDownCounter<int>("updown", () => new Measurement<int>(12));
                listener.RecordObservableInstruments();
                break;
            case 3:
                meter.CreateObservableCounter<int>("counter", () => new[] { new Measurement<int>(7) });
                meter.CreateObservableGauge<int>("gauge", () => new[] { new Measurement<int>(10) });
                meter.CreateObservableUpDownCounter<int>("updown", () => new[] { new Measurement<int>(13) });
                listener.RecordObservableInstruments();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }

        return ((ulong)(uint)published << 32) | (uint)measurements;
    }

    [WitExport("netwasm:diagnostics@1.0.0/acceptance", "boundary")]
    public static ulong Boundary()
    {
        using var activity = new Activity("exception-boundary").Start();
        var previous = Activity.Current;
        var rejected = false;
        try
        {
            activity!.AddException(new InvalidOperationException("portable boundary"));
        }
        catch (PlatformNotSupportedException)
        {
            rejected = true;
        }

        var events = activity!.Events.GetEnumerator();
        var noMutation = !events.MoveNext();
        events.Dispose();
        return (rejected ? 1UL << 0 : 0) |
            (ReferenceEquals(previous, activity) ? 1UL << 1 : 0) |
            (noMutation ? 1UL << 2 : 0);
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

    private sealed class RecordingObserver(
        List<string> callbacks,
        List<string>? publicationOrder = null) : IObserver<KeyValuePair<string, object?>>
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
            publicationOrder?.Add("diagnostic:" + value.Key);
        }
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
