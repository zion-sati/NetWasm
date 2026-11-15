// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticJsonLineFormatterTests
{
    [Fact]
    public void FormatWritesEveryDiagnosticFieldAsJson()
    {
        var formatter = Assert.IsAssignableFrom<ICompilerDiagnosticLineFormatter>(
            new CompilerDiagnosticJsonLineFormatter());
        var timestamp = new DateTimeOffset(2026, 8, 25, 1, 2, 3, TimeSpan.Zero);
        var entry = new CompilerDiagnosticEntry(
            "category",
            LogLevel.Error,
            new EventId(42, "event"),
            "message",
            "exception-type",
            "exception-message",
            "diagnostic-stack");

        using var document = JsonDocument.Parse(formatter.Format(7, timestamp, entry));
        var root = document.RootElement;

        Assert.Equal(7, root.GetProperty("Sequence").GetInt64());
        Assert.Equal(timestamp, root.GetProperty("TimestampUtc").GetDateTimeOffset());
        Assert.Equal("category", root.GetProperty("Category").GetString());
        Assert.Equal((int)LogLevel.Error, root.GetProperty("Level").GetInt32());
        Assert.Equal(42, root.GetProperty("EventId").GetInt32());
        Assert.Equal("event", root.GetProperty("EventName").GetString());
        Assert.Equal("message", root.GetProperty("Message").GetString());
        Assert.Equal("exception-type", root.GetProperty("ExceptionType").GetString());
        Assert.Equal("exception-message", root.GetProperty("ExceptionMessage").GetString());
        Assert.Equal("diagnostic-stack", root.GetProperty("ExceptionStackTrace").GetString());
    }

    [Fact]
    public void FormatRejectsANullEntry()
    {
        var formatter = Assert.IsAssignableFrom<ICompilerDiagnosticLineFormatter>(
            new CompilerDiagnosticJsonLineFormatter());

        Assert.Throws<ArgumentNullException>(() => formatter.Format(1, DateTimeOffset.UtcNow, null!));
    }
}
