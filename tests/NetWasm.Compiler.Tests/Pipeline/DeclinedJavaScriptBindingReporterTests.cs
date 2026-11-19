// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Pipeline;

namespace NetWasm.Compiler.Tests.Pipeline;

// Where a JavaScript host runs the program, each generated binding NetWasm's interop declines warns once, in name
// order, with what follows: JavaScript cannot call such an export, and such an import throws when called.
public sealed class DeclinedJavaScriptBindingReporterTests
{
    private static readonly AssemblyIdentity Assembly = new("Bindings");

    [Fact]
    public void EachDeclinedBindingWarnsWithWhatFollows()
    {
        var logger = new Logger(enabled: true);

        new DeclinedJavaScriptBindingReporter(logger).Report(Metadata(), new Symbols(), javaScriptHost: true);

        Assert.Equal(2, logger.Messages.Count);
        Assert.Contains("Lib.Exported", logger.Messages[0], StringComparison.Ordinal);
        Assert.Contains("JavaScript cannot call it", logger.Messages[0], StringComparison.Ordinal);
        Assert.Contains("Lib.Imported", logger.Messages[1], StringComparison.Ordinal);
        Assert.Contains("calling it throws PlatformNotSupportedException", logger.Messages[1], StringComparison.Ordinal);
    }

    // Without a JavaScript host the bindings stay ordinary code anyway; a logger not showing warnings hears nothing.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NothingWarnsWithoutAHostOrAListener(bool javaScriptHost, bool enabled)
    {
        var logger = new Logger(enabled);

        new DeclinedJavaScriptBindingReporter(logger).Report(Metadata(), new Symbols(), javaScriptHost);

        Assert.Empty(logger.Messages);
        Assert.Throws<ArgumentNullException>(() => new DeclinedJavaScriptBindingReporter(logger).Report(null!, new Symbols(), true));
        Assert.Throws<ArgumentNullException>(() => new DeclinedJavaScriptBindingReporter(logger).Report(Metadata(), null!, true));
    }

    private static MetadataCompilationSnapshot Metadata()
    {
        MethodDefinitionModel Method(int token, string name, DeclinedJavaScriptBinding? declined)
        {
            var method = new MethodDefinitionModel(new EntityKey(Assembly, token), new EntityKey(Assembly, 0x02000001),
                name, true, MethodSignatureModel.Create(CliValueKind.Void), 1);
            return method with { DeclinedJavaScriptBinding = declined };
        }

        return new MetadataCompilationSnapshot([], Assembly, [],
            [Method(0x06000001, "Imported", new("JSImport", "it takes a span")), Method(0x06000002, "Plain", null),
                Method(0x06000003, "Exported", new("JSExport", "it takes a delegate"))],
            [], ImmutableDictionary<string, string>.Empty);
    }

    private sealed class Symbols : ISymbolFormatter
    {
        public string Format(EntityKey key) => key.ToString();

        public string Format(MethodDefinitionModel method) => $"Lib.{method.Name}";
    }

    private sealed class Logger(bool enabled) : ILogger<DeclinedJavaScriptBindingReporter>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => enabled;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
