// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Threading.Tasks;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests;

public sealed class ProcessSessionSubstitutabilityTests
{
    [Fact]
    public async Task SystemProcessAdaptersUseOnlyTheSessionCapabilities()
    {
        var session = new RecordingSession();

        session.Environment.RemoveByPrefix("NODE_");
        new SystemProcessArgumentAppender().Append(session, "--value");
        new SystemProcessStarter().Start(session);
        var output = await new SystemStandardOutputReader().ReadAsync(session);
        var error = await new SystemStandardErrorReader().ReadAsync(session);
        new SystemProcessWaiter().Wait(session);
        var exitCode = new SystemProcessExitCodeReader().Read(session);

        Assert.Equal(["NODE_"], session.Environment.Prefixes);
        Assert.Equal(["--value"], session.Arguments.Values);
        Assert.True(session.Starter.Started);
        Assert.Equal("output", output);
        Assert.Equal("error", error);
        Assert.True(session.Waiter.Waited);
        Assert.Equal(23, exitCode);
    }

    [Fact]
    public async Task SystemProcessAdaptersRejectMissingSessions()
    {
        Assert.Throws<ArgumentNullException>(() => new SystemProcessSession(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new SystemProcessArgumentAppender().Append(null!, "value"));
        Assert.Throws<ArgumentNullException>(() =>
            new SystemProcessStarter().Start(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new SystemStandardOutputReader().ReadAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new SystemStandardErrorReader().ReadAsync(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new SystemProcessWaiter().Wait(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new SystemProcessExitCodeReader().Read(null!));
    }

    private sealed class RecordingSession : IProcessSession
    {
        public RecordingEnvironmentTarget Environment { get; } = new();
        public RecordingArgumentTarget Arguments { get; } = new();
        public RecordingStartTarget Starter { get; } = new();
        public RecordingOutputTarget StandardOutput { get; } = new("output");
        public RecordingErrorTarget StandardError { get; } = new("error");
        public RecordingWaitTarget Waiter { get; } = new();
        public RecordingExitCodeTarget ExitCode { get; } = new(23);

        IProcessEnvironmentTarget IProcessSession.Environment => Environment;
        IProcessArgumentTarget IProcessSession.Arguments => Arguments;
        IProcessStartTarget IProcessSession.Starter => Starter;
        IProcessStandardOutputTarget IProcessSession.StandardOutput => StandardOutput;
        IProcessStandardErrorTarget IProcessSession.StandardError => StandardError;
        IProcessWaitTarget IProcessSession.Waiter => Waiter;
        IProcessExitCodeTarget IProcessSession.ExitCode => ExitCode;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingEnvironmentTarget : IProcessEnvironmentTarget
    {
        public List<string> Prefixes { get; } = [];

        public void RemoveByPrefix(string prefix) => Prefixes.Add(prefix);
    }

    private sealed class RecordingArgumentTarget : IProcessArgumentTarget
    {
        public List<string> Values { get; } = [];

        public void Append(string argument) => Values.Add(argument);
    }

    private sealed class RecordingStartTarget : IProcessStartTarget
    {
        public bool Started { get; private set; }

        public void Start() => Started = true;
    }

    private sealed class RecordingOutputTarget(string value) : IProcessStandardOutputTarget
    {
        public Task<string> ReadAsync() => Task.FromResult(value);
    }

    private sealed class RecordingErrorTarget(string value) : IProcessStandardErrorTarget
    {
        public Task<string> ReadAsync() => Task.FromResult(value);
    }

    private sealed class RecordingWaitTarget : IProcessWaitTarget
    {
        public bool Waited { get; private set; }

        public void Wait() => Waited = true;
    }

    private sealed class RecordingExitCodeTarget(int value) : IProcessExitCodeTarget
    {
        public int Read() => value;
    }
}
