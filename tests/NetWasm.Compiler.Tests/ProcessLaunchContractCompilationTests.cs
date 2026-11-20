// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// System.Diagnostics.Process's members newer than .NET Standard 2.1, as on .NET 11 where processes cannot be started
// (iOS and tvOS): each validates its arguments and the process's state as upstream does, and then throws
// PlatformNotSupportedException where it would start, signal or wait for a process. The start settings and the
// exit-status and output types are plain data. Microsoft.Testing.Platform's host controllers reach these members.
public sealed class ProcessLaunchContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void NewerProcessMembersValidateAndThenRejectThePlatform(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("ProcessLaunchFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ProcessLaunchFixture.EntryPoint",
            "Run", [new("run", "ProcessLaunchFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Diagnostics;
        using System.Runtime.InteropServices;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Win32.SafeHandles;
        namespace ProcessLaunchFixture;

        public static class EntryPoint
        {
            private static bool Throws<T>(Action action) where T : Exception
            {
                try { action(); return false; }
                catch (T) { return true; }
            }

            private static bool Faults<T>(Func<Task> start) where T : Exception
            {
                var task = start();
                return task.IsFaulted && task.Exception!.InnerException is T;
            }

            private static bool FailsOnEnumeration<T>(IAsyncEnumerable<ProcessOutputLine> lines) where T : Exception
            {
                var enumerator = lines.GetAsyncEnumerator();
                try
                {
                    var moved = enumerator.MoveNextAsync();
                    return moved.IsFaulted && moved.AsTask().Exception!.InnerException is T;
                }
                catch (T)
                {
                    return true;
                }
            }

            public static unsafe int Run(int input)
            {
                // Start settings.
                var info = new ProcessStartInfo("tool", new List<string> { "a b", "c" });
                if (info.ArgumentList.Count != 2 || info.ArgumentList[0] != "a b" || info.Arguments.Length != 0) return 1;
                if (!Throws<ArgumentNullException>(() => new ProcessStartInfo(null!, new List<string>()))) return 2;
                if (!Throws<ArgumentNullException>(() => new ProcessStartInfo("tool", (IEnumerable<string>)null!))) return 3;
                info.StartDetached = true;
                info.KillOnParentExit = true;
                if (!info.StartDetached || !info.KillOnParentExit || info.StandardInputHandle is not null || info.InheritedHandles is not null) return 4;
                if (!Throws<PlatformNotSupportedException>(() => _ = info.CreateNewProcessGroup)) return 5;
                if (!Throws<PlatformNotSupportedException>(() => info.UseCredentialsForNetworkingOnly = true)) return 6;

                // Start validates the settings before it rejects the platform.
                var detachedShell = new ProcessStartInfo("tool") { StartDetached = true, UseShellExecute = true };
                if (!Throws<InvalidOperationException>(() => Process.Start(detachedShell))) return 7;
                var killShell = new ProcessStartInfo("tool") { KillOnParentExit = true, UseShellExecute = true };
                if (!Throws<InvalidOperationException>(() => Process.Start(killShell))) return 8;
                var nullHandle = new ProcessStartInfo("tool") { InheritedHandles = new List<SafeHandle> { null! } };
                if (!Throws<ArgumentNullException>(() => Process.Start(nullHandle))) return 9;
                if (!Throws<PlatformNotSupportedException>(() => Process.Start("tool", new[] { "x" }))) return 10;

                // The scenarios check their settings, then cannot start anything.
                if (!Throws<ArgumentNullException>(() => Process.Run((ProcessStartInfo)null!))) return 11;
                if (!Throws<ArgumentException>(() => Process.Run(""))) return 12;
                if (!Throws<PlatformNotSupportedException>(() => Process.Run("tool", new[] { "x" }))) return 13;
                if (!Throws<InvalidOperationException>(() => Process.RunAndCaptureText(new ProcessStartInfo("tool")))) return 14;
                var shell = new ProcessStartInfo("tool") { UseShellExecute = true };
                if (!Throws<InvalidOperationException>(() => Process.StartAndForget(shell))) return 15;
                if (!Throws<PlatformNotSupportedException>(() => Process.StartAndForget("tool"))) return 16;
                if (!Throws<PlatformNotSupportedException>(() => Process.RunAndCaptureText("tool"))) return 17;
                if (!Faults<PlatformNotSupportedException>(() => Process.RunAsync("tool"))) return 18;
                if (!Faults<InvalidOperationException>(() => Process.RunAndCaptureTextAsync(new ProcessStartInfo("tool")))) return 19;

                // A Process with no associated process.
                using var process = new Process();
                if (!Throws<InvalidOperationException>(() => process.WaitForExit(TimeSpan.FromSeconds(1)))) return 20;
                if (!Throws<ArgumentOutOfRangeException>(() => process.WaitForExit(TimeSpan.FromMilliseconds(-2)))) return 21;
                if (!Faults<InvalidOperationException>(() => process.WaitForExitAsync())) return 22;
                if (!Throws<InvalidOperationException>(() => process.ReadAllText())) return 23;
                if (!Throws<InvalidOperationException>(() => process.ReadAllLines())) return 24;
                if (!Faults<InvalidOperationException>(() => process.ReadAllBytesAsync())) return 25;
                if (!FailsOnEnumeration<InvalidOperationException>(process.ReadAllLinesAsync())) return 26;
                if (!Throws<PlatformNotSupportedException>(() => process.Kill(entireProcessTree: true))) return 27;
                if (!Throws<PlatformNotSupportedException>(() => process.Signal(PosixSignal.SIGTERM))) return 28;
                if (!Throws<PlatformNotSupportedException>(() => process.WaitForExitStatus())) return 29;
                if (!Throws<PlatformNotSupportedException>(() => process.TryWaitForExitStatus(TimeSpan.Zero, out _))) return 30;
                if (!Faults<PlatformNotSupportedException>(() => process.WaitForExitStatusAsync())) return 31;
                if (!Throws<ArgumentOutOfRangeException>(() => Process.TryGetProcessById(0, out _))) return 32;

                // Process handles: argument checks, then no handle can exist.
                if (!Throws<ArgumentOutOfRangeException>(() => SafeProcessHandle.Open(0))) return 33;
                if (!Throws<PlatformNotSupportedException>(() => SafeProcessHandle.TryOpen(1, out _))) return 34;
                if (!Throws<ArgumentNullException>(() => SafeProcessHandle.Start(null!))) return 35;
                var redirected = new ProcessStartInfo("tool") { RedirectStandardOutput = true };
                if (!Throws<InvalidOperationException>(() => SafeProcessHandle.Start(redirected))) return 36;
                if (!Throws<PlatformNotSupportedException>(() => SafeProcessHandle.Start(new ProcessStartInfo("tool")))) return 37;
                if (!Throws<PlatformNotSupportedException>(() => new SafeProcessHandle())) return 38;
                if (!Throws<ArgumentNullException>(() => UnixProcessStartArguments.Start(null!, _ => 1))) return 39;
                if (!Throws<InvalidOperationException>(() => UnixProcessStartArguments.Start(shell, _ => 1))) return 40;
                if (!Throws<PlatformNotSupportedException>(() => WindowsProcessStartArguments.Start(info, _ => 1))) return 41;
                var arguments = default(UnixProcessStartArguments);
                if (arguments.ResolvedPath != null || arguments.StandardInput != 0 || arguments.ProcessStartInfo is not null) return 42;

                // Exit status and output.
                var status = new ProcessExitStatus(3, canceled: true, PosixSignal.SIGKILL);
                if (status.ExitCode != 3 || !status.Canceled || status.Signal != PosixSignal.SIGKILL || new ProcessExitStatus(0, false).Signal is not null) return 43;
                var text = new ProcessTextOutput(status, "out", "err", 7);
                if (text.ExitStatus != status || text.StandardOutput != "out" || text.StandardError != "err" || text.ProcessId != 7) return 44;
                if (!Throws<ArgumentNullException>(() => new ProcessTextOutput(null!, "", "", 0))) return 45;
                var line = new ProcessOutputLine("line", standardError: true);
                if (line.Content != "line" || !line.StandardError || line.ToString() != "line" || default(ProcessOutputLine).ToString() != "") return 46;
                if (!Throws<ArgumentNullException>(() => new ProcessOutputLine(null!, false))) return 47;
                return input + 1;
            }
        }
        """;
}
