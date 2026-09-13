using System;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedExceptionObjectProvider(
    ManagedLayoutSnapshot snapshot) : IManagedExceptionObjectProvider
{
    private readonly ManagedLayoutSnapshot _snapshot = snapshot ??
        throw new ArgumentNullException(nameof(snapshot));

    public int GetExceptionObject(ManagedExceptionKind kind) =>
        _snapshot.StaticData.ExceptionObjects.TryGetValue(kind, out var address)
            ? address
            : throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"implicit exception object '{kind}' was not generated"));
}
