// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Pipeline;

// Warns once for each library JavaScript binding .NET's browser binding generator wrote that NetWasm's interop does not
// take (DeclinedJavaScriptBinding), in a program a JavaScript host runs: the method stays ordinary code, so JavaScript
// cannot call such an export, and such an import throws PlatformNotSupportedException when called.
internal sealed class DeclinedJavaScriptBindingReporter(ILogger<DeclinedJavaScriptBindingReporter> logger)
    : IDeclinedJavaScriptBindingReporter
{
    private static readonly Action<ILogger, string, string, string, string, Exception?> LogDeclined =
        LoggerMessage.Define<string, string, string, string>(
            LogLevel.Warning,
            new EventId((int)DiagnosticCode.DeclinedJavaScriptBinding, "NW1018"),
            "{Method} carries a [{Attribute}] that .NET's browser binding generator wrote, whose shape NetWasm's " +
            "JavaScript interop does not take: {Reason}. The method stays ordinary code: {Consequence}.");

    public void Report(MetadataCompilationSnapshot metadata, ISymbolFormatter symbols, bool javaScriptHost)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(symbols);
        if (!javaScriptHost || !logger.IsEnabled(LogLevel.Warning))
            return;
        var declined = metadata.Methods
            .Where(static method => method.DeclinedJavaScriptBinding is not null)
            .Select(method => (Name: symbols.Format(method), Binding: method.DeclinedJavaScriptBinding!))
            .OrderBy(static method => method.Name, StringComparer.Ordinal);
        foreach (var (name, binding) in declined)
        {
            LogDeclined(logger, name, binding.Attribute, binding.Reason,
                binding.Attribute == "JSExport"
                    ? "JavaScript cannot call it"
                    : "calling it throws PlatformNotSupportedException", null);
        }
    }
}
