// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Tests;

public sealed class MethodBufferedLoggerTests
{
    [Fact]
    public void ReplaysMethodsAndNestedScopesInRequestedOrder()
    {
        var sink = new RecordingLogger();
        var logger = new MethodBufferedLogger(sink);
        var state = new List<KeyValuePair<string, object?>>
        {
            new("Method", "second"),
        };

        logger.BeginMethod(1);
        using (logger.BeginScope("outer"))
        {
            using (logger.BeginScope("inner"))
                logger.Log(LogLevel.Information, new EventId(42), state,
                    null, static (_, _) => "second message");
        }
        logger.EndMethod();
        logger.BeginMethod(0);
        logger.Log(LogLevel.Warning, new EventId(7), "first", null,
            static (message, _) => message);
        logger.EndMethod();
        state[0] = new("Method", "changed");

        Assert.Empty(sink.Events);
        logger.Replay(0);
        logger.Replay(1);
        logger.Replay(99);

        Assert.Equal([
            "Warning:7:first",
            "scope+:outer",
            "scope+:inner",
            "Information:42:second message",
            "scope-:inner",
            "scope-:outer",
        ], sink.Events);
        Assert.Equal("second", sink.States.Last().Single().Value);
    }

    [Fact]
    public void ReplayDisposesScopesWhenDownstreamThrows()
    {
        var sink = new RecordingLogger { ThrowOnLog = true };
        var logger = new MethodBufferedLogger(sink);
        logger.BeginMethod(0);
        using (logger.BeginScope("scope"))
            logger.Log(LogLevel.Information, new EventId(1), "message", null,
                static (message, _) => message);
        logger.EndMethod();

        Assert.Throws<InvalidOperationException>(() => logger.Replay(0));
        Assert.Equal(["scope+:scope", "scope-:scope"], sink.Events);
    }

    [Fact]
    public void DisabledLogDoesNotEnterBuffer()
    {
        var sink = new RecordingLogger { Enabled = false };
        var logger = new MethodBufferedLogger(sink);
        logger.Log(LogLevel.Debug, new EventId(1), "ignored", null,
            static (message, _) => message);
        logger.Replay(0);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void RejectsLogOutsideMethodAndConcurrentBegin()
    {
        var logger = new MethodBufferedLogger(new RecordingLogger());
        Assert.Throws<InvalidOperationException>(() => logger.BeginScope("scope"));
        Assert.Throws<InvalidOperationException>(() => logger.Log(
            LogLevel.Information, new EventId(1), "message", null,
            static (message, _) => message));
        logger.BeginMethod(0);
        Assert.Throws<InvalidOperationException>(() => logger.BeginMethod(1));
        logger.EndMethod();
    }

    [Fact]
    public void CapturedStatesSnapshotEverySupportedValueShape()
    {
        var sink = new RecordingLogger { ReturnNullScope = true };
        var logger = new MethodBufferedLogger(sink);
        var reference = new ReferenceValue();
        var state = new List<KeyValuePair<string, object?>>
        {
            new("Null", null),
            new("String", "text"),
            new("Value", 42),
            new("Reference", reference),
        };

        logger.BeginMethod(0);
        var scope = logger.BeginScope(state);
        scope.Dispose();
        scope.Dispose();
        using (logger.BeginScope(reference)) { }
        using (logger.BeginScope(new NullStringValue())) { }
        logger.Log(LogLevel.Information, new EventId(1), state, null,
            static (value, _) => $"{value.Count} values");
        logger.EndMethod();
        logger.Replay(0);

        var captured = Assert.Single(sink.States);
        Assert.Equal(4, captured.Count);
        Assert.Null(captured[0].Value);
        Assert.Equal("text", captured[1].Value);
        Assert.Equal(42, captured[2].Value);
        Assert.Equal(nameof(ReferenceValue), captured[3].Value);
        Assert.Equal("4 values", captured.ToString());
        Assert.Equal(4, ((System.Collections.IEnumerable)captured)
            .Cast<object>().Count());
    }

    private sealed class RecordingLogger : ILogger<WasmModuleEmitterFactory>
    {
        public List<string> Events { get; } = [];
        public List<IReadOnlyList<KeyValuePair<string, object?>>> States
        { get; } = [];
        public bool Enabled { get; set; } = true;
        public bool ThrowOnLog { get; set; }
        public bool ReturnNullScope { get; set; }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            var name = state.ToString() ?? string.Empty;
            Events.Add($"scope+:{name}");
            return ReturnNullScope
                ? null!
                : new Scope(() => Events.Add($"scope-:{name}"));
        }

        public bool IsEnabled(LogLevel logLevel) => Enabled;

        public void Log<TState>(LogLevel logLevel, EventId eventId,
            TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (ThrowOnLog) throw new InvalidOperationException("log failed");
            Events.Add($"{logLevel}:{eventId.Id}:{formatter(state, exception)}");
            if (state is IReadOnlyList<KeyValuePair<string, object?>> pairs)
                States.Add(pairs);
        }

        private sealed class Scope(Action end) : IDisposable
        {
            public void Dispose() => end();
        }
    }

    private sealed class ReferenceValue
    {
        public override string ToString() => nameof(ReferenceValue);
    }

    private sealed class NullStringValue
    {
        public override string? ToString() => null;
    }
}
