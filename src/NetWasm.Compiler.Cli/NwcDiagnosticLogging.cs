using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli;

internal static class NwcDiagnosticLogging
{
    // Logs a failure as an error whose event names its diagnostic id, which nwc's
    // console writes as "error NW1234: message".
    public static void Report(this ILogger logger, CompilerDiagnostic diagnostic) => logger.Log(
        LogLevel.Error,
        new EventId((int)diagnostic.Code, diagnostic.Id),
        diagnostic.ToString()[(diagnostic.Id.Length + 2)..],
        null,
        static (text, _) => text);
}
