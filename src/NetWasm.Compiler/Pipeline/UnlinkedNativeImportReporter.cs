using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Pipeline;

// Warns once for each reached P/Invoke whose library the program does not link: it compiles to a body that throws
// DllNotFoundException (NativeLibraryBinding), so a call that runs fails as Mono's WebAssembly build fails one.
internal sealed class UnlinkedNativeImportReporter(ILogger<UnlinkedNativeImportReporter> logger)
    : IUnlinkedNativeImportReporter
{
    private static readonly Action<ILogger, string, string, string, Exception?> LogUnlinked =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId((int)DiagnosticCode.UnlinkedNativeLibrary, "NW1017"),
            "{Method} imports '{EntryPoint}' from native library '{Library}', which the application does not link; " +
            "calling it throws DllNotFoundException. Reference the package or NativeLibrary item that provides it, " +
            "or keep the call unreached.");

    public void Report(ReachableProgram program, ISymbolFormatter symbols)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(symbols);
        if (!logger.IsEnabled(LogLevel.Warning))
            return;
        var unlinked = program.Methods.Values
            .Select(static body => body.Method.Definition)
            .Where(static method => method.UnboundNativeImport is not null)
            .DistinctBy(static method => method.Key)
            .Select(method => (Name: symbols.Format(method), Import: method.UnboundNativeImport!))
            .OrderBy(static method => method.Name, StringComparer.Ordinal);
        foreach (var (name, import) in unlinked)
            LogUnlinked(logger, name, import.EntryPoint, import.LibraryName, null);
    }
}
