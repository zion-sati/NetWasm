using System;
using System.Threading;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticScopePusher(CompilerDiagnosticScopeState state)
    : ICompilerDiagnosticScopePusher
{
    public IDisposable Push(CompilerDiagnosticScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var frame = new CompilerDiagnosticScopeFrame(scope, state.Current.Value);
        state.Current.Value = frame;
        return new CompilerDiagnosticScopeLease(state, frame);
    }

    private sealed class CompilerDiagnosticScopeLease(
        CompilerDiagnosticScopeState state,
        CompilerDiagnosticScopeFrame frame) : IDisposable
    {
        private readonly Lock _gate = new();
        private bool _disposed;

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (!ReferenceEquals(state.Current.Value, frame))
                {
                    throw new InvalidOperationException("Compiler diagnostic scopes must be disposed in reverse order.");
                }

                state.Current.Value = frame.Parent;
                _disposed = true;
            }
        }
    }
}
