using System;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticClock
{
    DateTimeOffset Read();
}
