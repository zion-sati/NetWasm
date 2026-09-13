using System;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticClock : ICompilerDiagnosticClock
{
    public DateTimeOffset Read() => TimeProvider.System.GetUtcNow();
}
