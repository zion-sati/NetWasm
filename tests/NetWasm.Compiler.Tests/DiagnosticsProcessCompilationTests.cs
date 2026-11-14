using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class DiagnosticsProcessCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ProcessSettingsStackContainersAndSymbolContractsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("DiagnosticsProcessFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "DiagnosticsProcessFixture.EntryPoint",
            "Run", [new("run", "DiagnosticsProcessFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    internal const string Source = """
        using System;
        using System.Collections;
        using System.ComponentModel;
        using System.Diagnostics;
        using System.Diagnostics.SymbolStore;
        using System.Security;
        using System.Text;
        using Microsoft.Win32.SafeHandles;
        namespace DiagnosticsProcessFixture;

        sealed class Sync : ISynchronizeInvoke
        {
            public bool Required;
            public Delegate? Posted;
            public object?[]? Arguments;
            public bool InvokeRequired => Required;
            public IAsyncResult BeginInvoke(Delegate method, object?[]? args) { Posted = method; Arguments = args; return null!; }
            public object? EndInvoke(IAsyncResult result) => null;
            public object? Invoke(Delegate method, object?[]? args) => null;
        }

        sealed class Observed : Process
        {
            public void Raise() => OnExited();
        }

        sealed class DocumentWriter : ISymbolDocumentWriter
        {
            public byte[]? Source;
            public Guid Algorithm;
            public void SetCheckSum(Guid algorithmId, byte[] checkSum) => Algorithm = algorithmId;
            public void SetSource(byte[] source) => Source = source;
        }

        public static class EntryPoint
        {
            static bool Mentions(Action action, string text)
            {
                try { action(); }
                catch (PlatformNotSupportedException exception)
                {
                    return exception.Message.Contains(text, StringComparison.OrdinalIgnoreCase);
                }
                return false;
            }
            static bool Throws<T>(Action action) where T : Exception
            {
                try { action(); }
                catch (T) { return true; }
                catch (Exception) { return false; }
                return false;
            }

            public static int Run(int input)
            {
                // ProcessStartInfo holds launch settings as managed state.
                var info = new ProcessStartInfo();
                if (info.FileName != "" || info.Arguments != "" || info.WorkingDirectory != "" || info.UserName != "" || info.Verb != "") return 1;
                if (info.UseShellExecute || info.CreateNoWindow || info.ErrorDialog || info.ErrorDialogParentHandle != IntPtr.Zero) return 2;
                if (info.RedirectStandardInput || info.RedirectStandardOutput || info.RedirectStandardError || info.StandardOutputEncoding != null) return 3;
                if (info.WindowStyle != ProcessWindowStyle.Normal || info.Verbs.Length != 0 || info.ArgumentList.Count != 0) return 4;
                var tool = new ProcessStartInfo("tool", "-a b") { WorkingDirectory = "/work", Verb = "open", WindowStyle = ProcessWindowStyle.Hidden };
                if (tool.FileName != "tool" || tool.Arguments != "-a b" || tool.WorkingDirectory != "/work" || tool.Verb != "open" || tool.WindowStyle != ProcessWindowStyle.Hidden) return 5;
                tool.FileName = null;
                tool.Arguments = null;
                if (tool.FileName != "" || tool.Arguments != "") return 6;
                if (!Throws<InvalidEnumArgumentException>(() => tool.WindowStyle = (ProcessWindowStyle)7)) return 7;
                tool.StandardErrorEncoding = Encoding.UTF8;
                tool.ArgumentList.Add("x");
                tool.ArgumentList.Add("y z");
                if (tool.ArgumentList.Count != 2 || tool.ArgumentList[1] != "y z" || tool.StandardErrorEncoding != Encoding.UTF8) return 8;

                // Environment snapshots the guest environment with ordinal keys; EnvironmentVariables wraps the same dictionary.
                var settings = new ProcessStartInfo("tool");
                var environment = settings.Environment;
                if (!ReferenceEquals(environment, settings.Environment)) return 9;
                environment["NETWASM_FIXTURE"] = "one";
                var variables = settings.EnvironmentVariables;
                if (variables["NETWASM_FIXTURE"] != "one" || environment.ContainsKey("netwasm_fixture") || variables.Count != environment.Count) return 10;
                if (!Throws<ArgumentException>(() => variables.Add("NETWASM_FIXTURE", "two"))) return 11;
                variables["NETWASM_FIXTURE"] = "two";
                if (environment["NETWASM_FIXTURE"] != "two") return 12;
                variables.Remove("NETWASM_FIXTURE");
                if (environment.ContainsKey("NETWASM_FIXTURE") || Environment.GetEnvironmentVariable("NETWASM_FIXTURE") != null) return 13;

                // Windows credential settings are rejected on this platform, as on Unix.
                if (!Throws<PlatformNotSupportedException>(() => _ = settings.Domain)) return 14;
                if (!Throws<PlatformNotSupportedException>(() => settings.LoadUserProfile = true)) return 15;
                if (!Throws<PlatformNotSupportedException>(() => _ = settings.Password)) return 16;
                if (!Throws<PlatformNotSupportedException>(() => settings.PasswordInClearText = "p")) return 17;

                // Start validates StartInfo before it needs a host process.
                if (!Throws<ArgumentNullException>(() => Process.Start((ProcessStartInfo)null!))) return 18;
                if (!Throws<InvalidOperationException>(() => Process.Start(""))) return 19;
                if (!Throws<InvalidOperationException>(() => Process.Start(new ProcessStartInfo("tool") { StandardOutputEncoding = Encoding.UTF8 }))) return 20;
                if (!Throws<InvalidOperationException>(() => Process.Start(new ProcessStartInfo("tool") { UseShellExecute = true, RedirectStandardOutput = true }))) return 21;
                var both = new ProcessStartInfo("tool", "a");
                both.ArgumentList.Add("b");
                if (!Throws<InvalidOperationException>(() => Process.Start(both))) return 22;
                var missing = new ProcessStartInfo("tool");
                missing.ArgumentList.Add(null!);
                if (!Throws<ArgumentNullException>(() => Process.Start(missing))) return 23;
                if (!Throws<PlatformNotSupportedException>(() => Process.Start("tool", "user", new SecureString(), "domain"))) return 24;

                // An unassociated Process is a managed component: configuration, events and cleanup work.
                var process = new Process();
                if (process.StartInfo == null || process.EnableRaisingEvents || process.SynchronizingObject != null) return 25;
                process.StartInfo = settings;
                if (!ReferenceEquals(process.StartInfo, settings) || !Throws<ArgumentNullException>(() => process.StartInfo = null!)) return 26;
                process.EnableRaisingEvents = true;
                // An unassociated Process reads as its type (System.Diagnostics.Process, or its ID where the program
                // carries no type names, issue 225).
                if (!process.EnableRaisingEvents || process.ToString() != process.GetType().ToString()) return 27;
                DataReceivedEventHandler received = (sender, e) => { };
                process.OutputDataReceived += received;
                process.ErrorDataReceived += received;
                process.OutputDataReceived -= received;

                // It has no process, so process queries throw InvalidOperationException as upstream does.
                if (!Throws<InvalidOperationException>(() => _ = process.Id)) return 28;
                if (!Throws<InvalidOperationException>(() => _ = process.HasExited)) return 29;
                if (!Throws<InvalidOperationException>(() => _ = process.ExitCode)) return 30;
                if (!Throws<InvalidOperationException>(() => _ = process.MachineName)) return 31;
                if (!Throws<InvalidOperationException>(() => _ = process.ProcessName)) return 32;
                if (!Throws<InvalidOperationException>(() => _ = process.StartTime)) return 33;
                if (!Throws<InvalidOperationException>(() => _ = process.TotalProcessorTime)) return 34;
                if (!Throws<InvalidOperationException>(() => _ = process.WorkingSet64)) return 35;
                if (!Throws<InvalidOperationException>(() => _ = process.Modules)) return 36;
                if (!Throws<InvalidOperationException>(() => _ = process.Threads)) return 37;
                if (!Throws<InvalidOperationException>(() => _ = process.PriorityClass)) return 38;
                if (!Throws<InvalidEnumArgumentException>(() => process.PriorityClass = (ProcessPriorityClass)1)) return 39;
                if (!Throws<InvalidOperationException>(() => _ = process.MaxWorkingSet)) return 40;
                if (!Throws<InvalidOperationException>(() => _ = process.SafeHandle)) return 41;
                if (!Throws<InvalidOperationException>(() => _ = process.StandardOutput)) return 42;
                if (!Throws<InvalidOperationException>(() => _ = process.StandardInput)) return 43;
                if (!Throws<InvalidOperationException>(() => process.CancelErrorRead())) return 44;
                if (!Throws<InvalidOperationException>(() => process.BeginOutputReadLine())) return 45;
        #if !DESKTOP_ORACLE
                // The pinned upstream validates the timeout before the association; desktop .NET 10 checks the association first.
                if (!Throws<ArgumentOutOfRangeException>(() => process.WaitForExit(-2))) return 46;
        #endif
                if (!Throws<InvalidOperationException>(() => process.WaitForExit(0))) return 47;
                if (!Throws<ArgumentException>(() => Process.GetProcessById(1, ""))) return 48;
                if (!Throws<PlatformNotSupportedException>(() => Process.GetProcesses("netwasm-remote-host"))) return 49;
                process.Refresh();
                process.Close();
                process.Dispose();
                if (!Throws<ObjectDisposedException>(() => _ = process.StandardError)) return 50;

                // OnExited dispatches directly or through the synchronizing object, as upstream does.
                var observed = new Observed();
                var exits = 0;
                EventHandler exited = (sender, e) => { if (ReferenceEquals(sender, observed) && e == EventArgs.Empty) exits++; };
                observed.Exited += exited;
                observed.Raise();
                var sync = new Sync();
                observed.SynchronizingObject = sync;
                observed.Raise();
                sync.Required = true;
                observed.Raise();
                if (exits != 2 || !ReferenceEquals(sync.Posted, exited) || sync.Arguments!.Length != 2 || !ReferenceEquals(sync.Arguments[0], observed)) return 51;
                observed.Exited -= exited;
                sync.Required = false;
                observed.Raise();
                if (exits != 2) return 52;

                // Process containers wrap caller-supplied arrays.
                var threads = new ProcessThreadCollection(new ProcessThread[] { null! });
                if (threads.Count != 1 || threads[0] != null || threads.IndexOf(null!) != 0 || !threads.Contains(null!)) return 53;
                threads.Insert(0, null!);
                if (threads.Add(null!) != 2 || threads.Count != 3) return 54;
                threads.Remove(null!);
                var threadCopy = new ProcessThread[3];
                threads.CopyTo(threadCopy, 1);
                if (threads.Count != 2 || threadCopy[0] != null) return 55;
                var modules = new ProcessModuleCollection(new ProcessModule[] { null!, null! });
                var moduleCopy = new ProcessModule[2];
                modules.CopyTo(moduleCopy, 0);
                if (modules.Count != 2 || modules[1] != null || modules.IndexOf(null!) != 0 || !modules.Contains(null!)) return 56;

                // Passive enums and attribute state.
                if ((int)ThreadState.Wait != 5 || (int)ThreadWaitReason.EventPairLow != 8 || (int)ThreadPriorityLevel.TimeCritical != 15) return 57;
                if ((int)ProcessPriorityClass.High != 0x80 || (int)ProcessPriorityClass.AboveNormal != 0x8000 || (int)ProcessWindowStyle.Maximized != 3) return 58;
                if (new MonitoringDescriptionAttribute("monitored").Description != "monitored") return 59;

                // StackTrace over a supplied frame is a managed container; capture constructors validate first,
                // and a trace's text leaves out frames without a method, as upstream's does.
                var trace = new StackTrace((StackFrame)null!);
                if (trace.FrameCount != 1 || trace.GetFrame(0) != null || trace.GetFrame(1) != null || trace.GetFrame(-1) != null) return 60;
                var frames = trace.GetFrames();
                if (frames.Length != 1 || frames[0] != null || ReferenceEquals(frames, trace.GetFrames())) return 61;
                if (trace.ToString() != Environment.NewLine) return 87;
                if (StackFrame.OFFSET_UNKNOWN != -1 || StackTrace.METHODS_TO_SKIP != 0) return 62;
                if (!Throws<ArgumentOutOfRangeException>(() => new StackTrace(-1))) return 63;
                if (!Throws<ArgumentNullException>(() => new StackTrace((Exception)null!))) return 64;
                if (!Throws<ArgumentOutOfRangeException>(() => new StackTrace(new Exception(), -1, true))) return 65;
                StackFrame? none = null;
                if (none!.GetNativeIP() != IntPtr.Zero || none!.GetNativeImageBase() != IntPtr.Zero || none!.HasNativeImage()) return 66;

                // Symbol store contracts are interfaces and values.
                var token = new SymbolToken(0x06000001);
                if (token.GetToken() != 0x06000001 || token != new SymbolToken(0x06000001) || token == new SymbolToken(2)) return 67;
                if (!token.Equals((object)new SymbolToken(0x06000001)) || token.Equals((object)"x") || token.GetHashCode() != 0x06000001) return 68;
                if (SymLanguageType.CSharp != new Guid("3f5162f8-07c6-11d3-9053-00c04fa302a1") || SymLanguageVendor.Microsoft != new Guid("994b45c4-e6e9-11d2-903f-00c04fa302a1")) return 69;
                if (SymDocumentType.Text != new Guid("5a869d0b-6611-11d3-bd2a-0000f80849bd") || (int)SymAddressKind.NativeOffset != 5 || (int)SymAddressKind.BitField != 9) return 70;
                ISymbolDocumentWriter writer = new DocumentWriter();
                writer.SetSource(new byte[] { 1, 2 });
                writer.SetCheckSum(SymLanguageType.Basic, new byte[0]);
                if (((DocumentWriter)writer).Source!.Length != 2 || ((DocumentWriter)writer).Algorithm != SymLanguageType.Basic) return 71;

        #if !DESKTOP_ORACLE
                // NetWasm has no host processes, stack capture, process handles or file metadata loader.
                if (!Throws<PlatformNotSupportedException>(() => Process.Start("tool"))) return 72;
                if (!Throws<PlatformNotSupportedException>(() => Process.GetCurrentProcess())) return 73;
                if (!Throws<PlatformNotSupportedException>(() => Process.GetProcesses())) return 74;
                if (!Throws<PlatformNotSupportedException>(() => Process.GetProcessesByName("tool"))) return 75;
                if (!Throws<PlatformNotSupportedException>(() => Process.GetProcessById(1))) return 76;
                if (!Throws<PlatformNotSupportedException>(() => Process.EnterDebugMode())) return 77;
                var host = new Process();
                if (!Throws<PlatformNotSupportedException>(() => host.Kill())) return 78;
                if (!Throws<PlatformNotSupportedException>(() => _ = host.MainWindowHandle)) return 79;
                // Each rejection says what is missing rather than the generic text.
                if (!Mentions(() => Process.Start("tool"), "process") || !Mentions(() => Process.GetCurrentProcess(), "process") ||
                    !Mentions(() => host.Kill(), "process")) return 99;
                if (!Throws<PlatformNotSupportedException>(() => _ = host.Responding)) return 80;
                if (!Throws<PlatformNotSupportedException>(() => _ = host.PriorityBoostEnabled)) return 81;
                if (!Throws<PlatformNotSupportedException>(() => host.WaitForInputIdle())) return 82;
                if (!Throws<PlatformNotSupportedException>(() => new StackTrace())) return 83;
                if (!Throws<PlatformNotSupportedException>(() => new StackTrace(new Exception(), true))) return 84;
                if (!Throws<PlatformNotSupportedException>(() => new StackFrame())) return 85;
                if (!Throws<PlatformNotSupportedException>(() => new StackFrame("file.cs", 3, 4))) return 86;
                if (!Throws<PlatformNotSupportedException>(() => new SafeProcessHandle(IntPtr.Zero, false))) return 88;
                // 0.12 has a file system, so a missing file fails upstream's existence check.
                if (!Throws<System.IO.FileNotFoundException>(() => FileVersionInfo.GetVersionInfo("/netwasm/missing.dll"))) return 89;
        #endif
                return input + 1;
            }
        }
        """;
}
