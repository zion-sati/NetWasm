using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedTracingCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TraceSourcesListenersSwitchesAndContractsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TracingFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TracingFixture.EntryPoint",
            "Run", [new("run", "TracingFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    internal const string Source = """
        #define DEBUG
        #define TRACE
        #define CONTRACTS_FULL
        using System;
        using System.Collections;
        using System.Diagnostics;
        using System.Diagnostics.Contracts;
        using System.IO;
        namespace TracingFixture;
        sealed class SuppliedAssembly : System.Reflection.Assembly { }
        sealed class Recording : TraceListener
        {
            public readonly System.Text.StringBuilder Text = new System.Text.StringBuilder();
            public Recording(string name) : base(name) { }
            public override void Write(string? message) => Text.Append(message);
            public override void WriteLine(string? message) => Text.Append(message).Append('|');
        }
        public static class EntryPoint
        {
            static bool Throws<T>(Action action) where T : Exception
            {
                try { action(); return false; } catch (T) { return true; }
            }

            public static int Run(int input)
            {
                // Switches parse values, levels and descriptions as upstream.
                var flag = new BooleanSwitch("flag", "description", "1");
                if (!flag.Enabled || flag.DisplayName != "flag" || flag.Description != "description" || flag.Value != "1") return 1;
                flag.Enabled = false;
                if (flag.Enabled || flag.Value != "1") return 2;
                var level = new TraceSwitch("level", "d", "Warning");
                if (level.Level != TraceLevel.Warning || !level.TraceError || !level.TraceWarning || level.TraceInfo || level.TraceVerbose) return 3;
                level.Level = TraceLevel.Verbose;
                if (!level.TraceVerbose || level.Value != "Verbose") return 4;
                if (!Throws<ArgumentException>(() => level.Level = (TraceLevel)9)) return 5;
                var source = new SourceSwitch("source", "Error");
                if (source.Level != SourceLevels.Error || !source.ShouldTrace(TraceEventType.Critical) || source.ShouldTrace(TraceEventType.Warning)) return 6;
                var switchMetadata = new SwitchAttribute("flag", typeof(BooleanSwitch)) { SwitchDescription = "d" };
                if (switchMetadata.SwitchName != "flag" || switchMetadata.SwitchType != typeof(BooleanSwitch) || switchMetadata.SwitchDescription != "d") return 36;
                if (!Throws<ArgumentException>(() => switchMetadata.SwitchName = "") || !Throws<ArgumentNullException>(() => switchMetadata.SwitchType = null!)) return 37;
                if (!Throws<ArgumentNullException>(() => SwitchAttribute.GetAll(null!))) return 38;

                // TraceSource formatting, filtering and data payloads.
                var output = new StringWriter();
                var traceSource = new TraceSource("src", SourceLevels.Warning);
                traceSource.Listeners.Clear();
                traceSource.Listeners.Add(new TextWriterTraceListener(output, "out"));
                traceSource.TraceEvent(TraceEventType.Warning, 3, "hello {0}", 1);
                traceSource.TraceEvent(TraceEventType.Information, 4, "filtered");
                traceSource.TraceData(TraceEventType.Error, 5, "a", 7);
                traceSource.Flush();
                string text = output.ToString().Replace("\r\n", "\n");
                if (text != "src Warning: 3 : hello 1\nsrc Error: 5 : a, 7\n") return 7;
                if (traceSource.Name != "src" || traceSource.Switch.Level != SourceLevels.Warning || traceSource.DefaultLevel != SourceLevels.Warning) return 8;
                var filtered = new StringWriter();
                var filteredListener = new TextWriterTraceListener(filtered) { Filter = new EventTypeFilter(SourceLevels.Error) };
                traceSource.Listeners.Add(filteredListener);
                traceSource.TraceEvent(TraceEventType.Warning, 6, "warn");
                traceSource.TraceEvent(TraceEventType.Critical, 7, "crit");
                filteredListener.Flush();
                if (filtered.ToString().Replace("\r\n", "\n") != "src Critical: 7 : crit\n") return 9;
                var sourceFilter = new SourceFilter("other");
                if (sourceFilter.ShouldTrace(null, "src", TraceEventType.Critical, 0, null, null, null, null) || !sourceFilter.ShouldTrace(null, "other", TraceEventType.Critical, 0, null, null, null, null)) return 10;
                if (!Throws<ArgumentNullException>(() => new SourceFilter(null!))) return 11;

                // Delimited listener escapes quoted fields and honours output options.
                var csv = new StringWriter();
                var delimited = new DelimitedListTraceListener(csv) { Delimiter = "," };
                delimited.TraceEvent(null, "s\"x", TraceEventType.Warning, 9, "a,b");
                delimited.Flush();
                if (csv.ToString().Replace("\r\n", "\n") != "\"s\"\"x\",Warning,9,\"a,b\",,,,,,,\n") return 12;
                if (!Throws<ArgumentException>(() => delimited.Delimiter = "")) return 13;

                // Trace and Debug route through the shared listener collection once Trace initializes.
                Trace.Listeners.Clear();
                var recording = new Recording("rec");
                Trace.Listeners.Add(recording);
                Trace.IndentSize = 2;
                Trace.WriteLine("one");
                Trace.Indent();
                Trace.WriteLine("two", "cat");
                Debug.WriteLine("three");
                Trace.Unindent();
                Trace.WriteLineIf(false, "skipped");
                Trace.Write(42);
                if (recording.Text.ToString() != "one|cat: two|three|42" || Trace.IndentLevel != 0 || Debug.IndentSize != 2) return 14;
                recording.Text.Clear();
                Trace.Fail("failure", "detail");
                Debug.Assert(false, "debug");
                if (recording.Text.ToString() != "Fail: failure detail|Fail: debug |") return 15;
                if (Trace.Listeners.Count != 1 || Trace.Listeners["rec"] != recording || !Trace.UseGlobalLock) return 16;

                // Correlation stacks and event cache metadata.
                var correlation = Trace.CorrelationManager;
                correlation.StartLogicalOperation("outer");
                correlation.StartLogicalOperation();
                if (correlation.LogicalOperationStack.Count != 2) return 17;
                correlation.StopLogicalOperation();
                if (!"outer".Equals(correlation.LogicalOperationStack.Peek())) return 18;
                correlation.StopLogicalOperation();
                if (!Throws<InvalidOperationException>(() => correlation.StopLogicalOperation())) return 19;
                var id = Guid.NewGuid();
                correlation.ActivityId = id;
                if (correlation.ActivityId != id) return 20;
                var cache = new TraceEventCache();
                if (cache.DateTime.Kind != DateTimeKind.Utc || cache.Timestamp == 0 || cache.ThreadId.Length == 0 || cache.LogicalOperationStack.Count != 0) return 21;

                // Contract helpers that do not need a binary rewriter.
                if (!Contract.ForAll(0, 5, i => i < 5) || Contract.Exists(new[] { 1, 2 }, i => i > 2) || !Contract.ForAll(new int[0], i => false)) return 22;
                if (!Throws<ArgumentException>(() => Contract.ForAll(3, 1, i => true))) return 23;
                if (Contract.Result<int>() != 0 || Contract.OldValue("x") != null) return 24;
                Contract.ValueAtReturn(out int returned);
                if (returned != 0) return 25;
                int seen = 0;
                EventHandler<ContractFailedEventArgs> handled = (s, e) => { seen++; if (e.FailureKind == ContractFailureKind.Assert && e.Condition == null) e.SetHandled(); };
                Contract.ContractFailed += handled;
                Contract.Assert(false, "handled");
                Contract.ContractFailed -= handled;
                if (seen != 1) return 26;
                EventHandler<ContractFailedEventArgs> unwinding = (s, e) => e.SetUnwind();
                Contract.ContractFailed += unwinding;
                bool unwound = false;
                // The private ContractException is the only exception here that derives from Exception directly.
                try { Contract.Assume(false, "unwind"); } catch (Exception e) when (e.GetType().BaseType == typeof(Exception)) { unwound = e.Message.Contains("unwind"); }
                Contract.ContractFailed -= unwinding;
                if (!unwound) return 27;
                var args = new ContractFailedEventArgs(ContractFailureKind.Invariant, "m", "c", null);
                if (args.Handled || args.Unwind || args.Message != "m" || args.Condition != "c" || args.FailureKind != ContractFailureKind.Invariant) return 28;
        #if !DESKTOP_ORACLE
                // NetWasm boundaries: no files, no process or stack metadata, and failed debug assertions throw
                // rather than terminating the process. Rewriter-dependent contracts fail as upstream's, through
                // ContractHelper.TriggerFailure, so they throw that failure here.
                if (!Throws<InvalidOperationException>(() => Contract.Requires(true))) return 29;
                if (!Throws<InvalidOperationException>(() => Contract.Ensures(true))) return 30;
                if (!Throws<InvalidOperationException>(() => Contract.Requires<ArgumentException>(true, "m"))) return 41;
                if (!Throws<InvalidOperationException>(() => Contract.EnsuresOnThrow<InvalidOperationException>(true))) return 44;
                if (!Throws<InvalidOperationException>(() => Contract.Invariant(true))) return 45;
                if (!Throws<InvalidOperationException>(() => Contract.Assert(false, "unhandled"))) return 31;
                // This program is given no file system, so neither listener can open its log file. As upstream, the
                // text writer listener then disables itself and the default listener reports the failure to the
                // debugger; neither throws.
                var fileListener = new TextWriterTraceListener("trace.log");
                fileListener.WriteLine("x");
                if (File.Exists("trace.log")) return 32;
                var defaultListener = new DefaultTraceListener { LogFileName = "trace.log", AssertUiEnabled = false };
                defaultListener.WriteLine("x");
                if (File.Exists("trace.log")) return 33;
                if (!Throws<PlatformNotSupportedException>(() => _ = cache.ProcessId)) return 34;
                if (!Throws<PlatformNotSupportedException>(() => _ = cache.Callstack)) return 39;
                if (!Throws<PlatformNotSupportedException>(() => SwitchAttribute.GetAll(new SuppliedAssembly()))) return 40;
                Trace.Listeners.Clear();
                Trace.Listeners.Add(new DefaultTraceListener());
                if (!Throws<InvalidOperationException>(() => Trace.Assert(false, "boom"))) return 35;
        #endif
                return input + 1;
            }
        }
        """;
}
