using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

/// <summary>Captures one worker's method events for ordered caller-thread replay.</summary>
internal sealed class MethodBufferedLogger(
    ILogger<WasmModuleEmitterFactory> downstream) :
    ILogger<WasmModuleEmitterFactory>
{
    private readonly Dictionary<int, List<BufferedLogEvent>> _events = [];
    private int _current = -1;

    public void BeginMethod(int index)
    {
        if (_current >= 0)
            throw new InvalidOperationException("A method log is already active.");
        _current = index;
        _events.Add(index, []);
    }

    public void EndMethod() => _current = -1;

    public void Replay(int index)
    {
        if (!_events.TryGetValue(index, out var events)) return;
        var replay = new ReplayState(downstream);
        try
        {
            foreach (var entry in events) replay.Replay(entry);
        }
        finally { replay.Dispose(); }
    }

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        var index = Current();
        var captured = CapturedLogState.CaptureScope(state);
        _events[index].Add(new BufferedLogEvent.ScopeBegin(captured));
        return new RecordedScope(() =>
            _events[index].Add(new BufferedLogEvent.ScopeEnd()));
    }

    public bool IsEnabled(LogLevel logLevel) => downstream.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId,
        TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        ArgumentNullException.ThrowIfNull(formatter);
        var index = Current();
        var captured = CapturedLogState.Capture(
            state, formatter(state, exception));
        _events[index].Add(new BufferedLogEvent.Message(
            logLevel, eventId, captured, exception));
    }

    private int Current() => _current >= 0
        ? _current
        : throw new InvalidOperationException(
            "Emission logger used outside a method buffer.");

    private sealed class RecordedScope(Action end) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            end();
        }
    }

    private abstract record BufferedLogEvent
    {
        internal sealed record ScopeBegin(object State) : BufferedLogEvent;
        internal sealed record ScopeEnd : BufferedLogEvent;
        internal sealed record Message(
            LogLevel Level,
            EventId EventId,
            CapturedLogState State,
            Exception? Exception) : BufferedLogEvent;
    }

    private sealed class ReplayState(
        ILogger<WasmModuleEmitterFactory> logger) : IDisposable
    {
        private readonly Stack<IDisposable> _scopes = new();

        internal void Replay(BufferedLogEvent entry)
        {
            if (entry is BufferedLogEvent.ScopeBegin begin)
            {
                _scopes.Push(logger.BeginScope(begin.State) ?? EmptyScope.Instance);
                return;
            }
            if (entry is BufferedLogEvent.ScopeEnd)
            {
                _scopes.Pop().Dispose();
                return;
            }
            var message = (BufferedLogEvent.Message)entry;
            logger.Log(message.Level, message.EventId, message.State,
                message.Exception,
                static (captured, _) => captured.Message);
        }

        public void Dispose()
        {
            while (_scopes.Count > 0)
                _scopes.Pop().Dispose();
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();
        public void Dispose() { }
    }

    private sealed class CapturedLogState(
        string message,
        ImmutableArray<KeyValuePair<string, object?>> values) :
        IReadOnlyList<KeyValuePair<string, object?>>
    {
        public string Message { get; } = message;
        public int Count => values.Length;
        public KeyValuePair<string, object?> this[int index] => values[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<string, object?>>)values).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public override string ToString() => Message;

        public static CapturedLogState Capture<TState>(TState state,
            string message) => new(message,
                state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? pairs.Select(pair => new KeyValuePair<string, object?>(
                        pair.Key, SnapshotValue(pair.Value))).ToImmutableArray()
                    : []);

        public static object CaptureScope<TState>(TState state)
            where TState : notnull
        {
            var message = state.ToString() ?? string.Empty;
            if (state is IEnumerable<KeyValuePair<string, object?>>)
                return Capture(state, message);
            return state is string text ? text : message;
        }

        private static object? SnapshotValue(object? value) =>
            value is null or string || value.GetType().IsValueType
                ? value
                : value.ToString();
    }
}
