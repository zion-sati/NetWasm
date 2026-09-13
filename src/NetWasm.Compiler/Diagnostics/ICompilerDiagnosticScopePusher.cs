using System;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticScopePusher
{
    IDisposable Push(CompilerDiagnosticScope scope);
}
