using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli;

internal static class CliOptionException
{
    public static CompilerException Create(string message) => new(
        new CompilerDiagnostic(DiagnosticCode.InvalidCommandLine, message));
}
