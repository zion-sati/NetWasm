using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedEventSourceCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DisabledEventSourceListenersAndCountersMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("EventSourceFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EventSourceFixture.EntryPoint",
            "Run", [new("run", "EventSourceFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    // The desktop oracle runs with EventSourceSupport=false, upstream's own disabled profile.
    internal const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.Tracing;
        namespace EventSourceFixture;
        [EventSource(Name = "Fixture-Source", Guid = "7f1c2f39-9f6e-4b1e-8c2d-1d3b5e8a9c01", LocalizationResources = "res")]
        sealed class FixtureSource : EventSource
        {
            public FixtureSource() { }
            public FixtureSource(EventSourceSettings settings, params string[] traits) : base(settings, traits) { }
            public void WriteAll()
            {
                WriteEvent(1);
                WriteEvent(2, 1);
                WriteEvent(3, 1, 2);
                WriteEvent(4, 1, 2, 3);
                WriteEvent(5, 1L);
                WriteEvent(6, 1L, 2L);
                WriteEvent(7, 1L, 2L, 3L);
                WriteEvent(8, "a");
                WriteEvent(9, "a", "b");
                WriteEvent(10, "a", "b", "c");
                WriteEvent(11, "a", 1);
                WriteEvent(12, "a", 1, 2);
                WriteEvent(13, "a", 1L);
                WriteEvent(14, 1L, "a");
                WriteEvent(15, 1, "a");
                WriteEvent(16, new byte[] { 1 });
                WriteEvent(17, 1L, new byte[0]);
                WriteEvent(18, 1.5, 'c', true, Guid.Empty);
                WriteEvent(19, new object[] { 1, "x" });
                WriteEventWithRelatedActivityId(20, Guid.Empty, 1, "x");
            }
            public static bool EventDataRoundTrips()
            {
                var data = new EventData { DataPointer = (IntPtr)8, Size = 4 };
                return data.DataPointer == (IntPtr)8 && data.Size == 4;
            }
            public static int ListenerIndex(EventSource source) => Listener.Index(source);
            protected override void OnEventCommand(EventCommandEventArgs command) => throw new InvalidOperationException();
        }
        sealed class Listener : EventListener
        {
            public int Created;
            public static int Index(EventSource source) => EventSourceIndex(source);
            protected override void OnEventSourceCreated(EventSource eventSource) => Created++;
            protected override void OnEventWritten(EventWrittenEventArgs eventData) => throw new InvalidOperationException();
        }
        [EventData(Name = "payload")]
        sealed class Payload
        {
            public static int Reads;
            public int Value { get { Reads++; return Reads; } }
        }
        public static class EntryPoint
        {
            static bool Throws<T>(Action action) where T : Exception
            {
                try { action(); return false; } catch (T) { return true; }
            }

            static bool ParamThrows(Action action, string name)
            {
                try { action(); return false; } catch (ArgumentNullException e) { return e.ParamName == name; }
            }

            public static int Run(int input)
            {
                // Passive metadata keeps upstream defaults, flags and checked conversions.
                var attribute = new EventAttribute(70000);
                if (attribute.EventId != 70000 || attribute.Level != EventLevel.Informational || attribute.Opcode != EventOpcode.Info) return 1;
                attribute.Opcode = EventOpcode.Start;
                attribute.Message = "m {0}";
                attribute.Tags = (EventTags)4;
                attribute.ActivityOptions = EventActivityOptions.Recursive | EventActivityOptions.Detachable;
                if (attribute.Opcode != EventOpcode.Start || attribute.Message != "m {0}" || attribute.Tags != (EventTags)4 || attribute.ActivityOptions != (EventActivityOptions)12) return 2;
                var sourceAttribute = new EventSourceAttribute { Name = "n", Guid = "g", LocalizationResources = "r" };
                if (sourceAttribute.Name != "n" || sourceAttribute.Guid != "g" || sourceAttribute.LocalizationResources != "r") return 3;
                var field = new EventFieldAttribute { Format = EventFieldFormat.Hexadecimal, Tags = (EventFieldTags)8 };
                if (field.Format != EventFieldFormat.Hexadecimal || field.Tags != (EventFieldTags)8) return 4;
                var data = new EventDataAttribute { Name = "d" };
                if (data.Name != "d" || new EventIgnoreAttribute() == null || new NonEventAttribute() == null) return 5;
                var options = new EventSourceOptions { Level = EventLevel.Warning, Opcode = EventOpcode.Stop, Keywords = (EventKeywords)3, Tags = (EventTags)1, ActivityOptions = EventActivityOptions.Disable };
                if (options.Level != EventLevel.Warning || options.Opcode != EventOpcode.Stop || options.Keywords != (EventKeywords)3 || options.ActivityOptions != EventActivityOptions.Disable) return 6;
                if (!Throws<OverflowException>(() => { var o = new EventSourceOptions(); o.Level = (EventLevel)256; })) return 7;
                if (!Throws<OverflowException>(() => { var o = new EventSourceOptions(); o.Opcode = (EventOpcode)300; })) return 8;
                if ((long)EventKeywords.All != -1 || (int)EventLevel.Verbose != 5 || (int)EventOpcode.Receive != 240 || (int)EventChannel.Debug != 19) return 9;
                if ((int)EventCommand.Disable != -3 || (int)EventSourceSettings.EtwSelfDescribingEventFormat != 8 || (int)EventManifestOptions.AllowEventSourceOverride != 8) return 10;
                if ((int)EventFieldFormat.Json != 12 || (int)EventTask.None != 0 || EventTags.None != 0 || EventFieldTags.None != 0) return 11;

                // A disabled source keeps its uninitialized state and never enables.
                var source = new FixtureSource();
                if (source.Name != null || source.Guid != Guid.Empty || source.Settings != EventSourceSettings.Default) return 12;
                if (source.IsEnabled() || source.IsEnabled(EventLevel.LogAlways, EventKeywords.All) || source.IsEnabled(EventLevel.Critical, EventKeywords.None, EventChannel.Admin)) return 13;
                if (source.ConstructionException != null || source.GetTrait("k") != null || source.ToString() != typeof(FixtureSource).ToString()) return 14;
                var traited = new FixtureSource(EventSourceSettings.ThrowOnEventWriteErrors, "odd");
                if (traited.Settings != EventSourceSettings.Default || traited.GetTrait("odd") != null) return 15;
                int commands = 0;
                EventHandler<EventCommandEventArgs> handler = (s, e) => commands++;
                source.EventCommandExecuted += handler;
                source.EventCommandExecuted += null;
                source.WriteAll();
                source.Write("plain");
                source.Write("options", new EventSourceOptions { Level = EventLevel.Error });
                source.Write("data", new Payload());
                source.Write("data", options, new Payload());
                var payload = new Payload();
                source.Write("ref", ref options, ref payload);
                var activity = Guid.NewGuid();
                var related = Guid.Empty;
                source.Write("activity", ref options, ref activity, ref related, ref payload);
                source.EventCommandExecuted -= handler;
                if (Payload.Reads != 0 || commands != 0 || !FixtureSource.EventDataRoundTrips()) return 16;

                // Named sources validate the name before forwarding to the disabled constructor.
                if (!ParamThrows(() => new EventSource(null!), "eventSourceName")) return 17;
                if (!ParamThrows(() => new EventSource(null!, EventSourceSettings.Default, "a", "b"), "eventSourceName")) return 18;
                var named = new EventSource("Named", EventSourceSettings.EtwManifestEventFormat, "k", "v");
                if (named.Name != null || named.Guid != Guid.Empty || named.GetTrait("k") != null) return 19;
                named.Write("x");
        #if !DESKTOP_ORACLE
                // The pinned upstream adds the explicit-GUID constructors after .NET 10, the desktop oracle.
                var explicitGuid = new EventSource("Explicit", Guid.Empty);
                var withSettings = new EventSource(null!, Guid.Empty, EventSourceSettings.ThrowOnEventWriteErrors, new[] { "odd" });
                if (explicitGuid.Name != null || withSettings.Settings != EventSourceSettings.Default) return 41;
        #endif

                // Static queries return the disabled results without validating their arguments.
                using (var sources = EventSource.GetSources().GetEnumerator())
                    if (sources.MoveNext()) return 20;
                if (EventSource.GenerateManifest(typeof(FixtureSource), null) != null || EventSource.GenerateManifest(null!, "a", EventManifestOptions.Strict) != null) return 21;
                EventSource.SendCommand(null!, (EventCommand)7, null);
                EventSource.SendCommand(source, EventCommand.Update, new Dictionary<string, string?>());
                if (EventSource.CurrentThreadActivityId != Guid.Empty) return 22;
                EventSource.SetCurrentThreadActivityId(activity);
                EventSource.SetCurrentThreadActivityId(activity, out Guid previous);
                if (previous != Guid.Empty || EventSource.CurrentThreadActivityId != Guid.Empty) return 23;
                if (!ParamThrows(() => EventSource.GetName(null!), "eventSourceType") || !ParamThrows(() => EventSource.GetGuid(null!), "eventSourceType")) return 24;
                if (new EventSourceCreatedEventArgs().EventSource != null) return 25;

                // Listeners register and dispose, but no source attaches and no command reaches one.
                var listener = new Listener();
                int created = 0;
                listener.EventSourceCreated += (s, e) => created++;
                listener.EventWritten += (s, e) => throw new InvalidOperationException();
                if (!ParamThrows(() => listener.EnableEvents(null!, EventLevel.Verbose), "eventSource")) return 26;
                if (!ParamThrows(() => listener.DisableEvents(null!), "eventSource")) return 27;
                listener.EnableEvents(source, EventLevel.Verbose);
                listener.EnableEvents(source, EventLevel.Informational, (EventKeywords)1);
                listener.EnableEvents(source, EventLevel.Error, EventKeywords.All, new Dictionary<string, string?> { ["EventCounterIntervalSec"] = "1" });
                source.WriteAll();
                listener.DisableEvents(source);
                var second = new Listener();
                if (listener.Created != 0 || second.Created != 0 || created != 0 || FixtureSource.ListenerIndex(source) != 0) return 28;
                listener.Dispose();
                second.Dispose();
                listener.Dispose();

                // Counters keep managed accumulators, metadata and validation; providers are never polled.
                if (!ParamThrows(() => new EventCounter(null!, source), "Name") || !ParamThrows(() => new EventCounter("c", null!), "EventSource")) return 29;
                var counter = new EventCounter("c", source);
                if (counter.Name != "c" || counter.EventSource != source || counter.DisplayName != "" || counter.DisplayUnits != "") return 30;
                counter.DisplayName = "Counter";
                counter.DisplayUnits = "ms";
                counter.AddMetadata("k", "v");
                if (!Throws<ArgumentException>(() => counter.AddMetadata("k", "w"))) return 31;
                if (counter.ToString() != "EventCounter 'c' Count 0" || counter.DisplayName != "Counter" || counter.DisplayUnits != "ms") return 32;
                for (int i = 1; i <= 12; i++)
                    counter.WriteMetric(i % 2 == 0 ? 2.0 : 3.0f);
                if (counter.ToString() != "EventCounter 'c' Count 10 Mean 2.500") return 33;
                var incrementing = new IncrementingEventCounter("i", source) { DisplayRateTimeScale = TimeSpan.FromSeconds(2) };
                incrementing.Increment();
                incrementing.Increment(2.5);
                if (incrementing.ToString() != "IncrementingEventCounter 'i' Increment 3.5" || incrementing.DisplayRateTimeScale != TimeSpan.FromSeconds(2)) return 34;
                int polls = 0;
                if (!ParamThrows(() => new PollingCounter("p", source, null!), "metricProvider")) return 35;
                if (!ParamThrows(() => new IncrementingPollingCounter("q", source, null!), "totalValueProvider")) return 36;
                var polling = new PollingCounter("p", source, () => ++polls);
                var total = new IncrementingPollingCounter("q", source, () => ++polls) { DisplayRateTimeScale = TimeSpan.FromMinutes(1) };
                if (polling.ToString() != "PollingCounter 'p' Count 1 Mean 0.000" || total.ToString() != "IncrementingPollingCounter 'q' Increment 0" || polls != 0) return 37;

                // Counters on two sources dispose cleanly in either order.
                var other = new FixtureSource();
                var otherCounter = new EventCounter("o", other);
                var otherPolling = new PollingCounter("op", other, () => ++polls);
                if (otherCounter.EventSource != other || otherPolling.EventSource != other || counter.EventSource != source) return 38;
                counter.Dispose();
                otherPolling.Dispose();
                incrementing.Dispose();
                otherCounter.Dispose();
                polling.Dispose();
                total.Dispose();
                counter.Dispose();
                var reused = new EventCounter("r", other);
                reused.Dispose();
                other.Dispose();
                source.Dispose();
                source.Dispose();
                if (source.IsEnabled() || polls != 0) return 39;

                // EventSourceException keeps its ordinary constructors.
                if (new EventSourceException("m").Message != "m" || new EventSourceException("m", new InvalidOperationException()).InnerException is not InvalidOperationException) return 40;
        #if !DESKTOP_ORACLE
                // NetWasm boundary: name and GUID discovery reads attributes through reflection.
                if (!Throws<PlatformNotSupportedException>(() => EventSource.GetName(typeof(FixtureSource)))) return 43;
                if (!Throws<PlatformNotSupportedException>(() => EventSource.GetGuid(typeof(FixtureSource)))) return 44;
        #endif
                return input + 1;
            }
        }
        """;
}
