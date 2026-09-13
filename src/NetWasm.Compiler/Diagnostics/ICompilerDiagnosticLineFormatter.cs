using System;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticLineFormatter
{
    string Format(long sequence, DateTimeOffset timestampUtc, CompilerDiagnosticEntry entry);
}
