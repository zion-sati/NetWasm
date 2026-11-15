// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.Logging;

namespace NetWasm.Compiler.Tests;

internal sealed class CompilerTestLogger<T> : ILogger<T>
{
    public int EntryCount { get; private set; }

    public LogLevel Level { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        EntryCount++;
        Level = logLevel;
        Message = formatter(state, exception);
    }
}
