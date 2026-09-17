using System;
using NetWasm.Compiler.Browser.Inputs;
using NetWasm.Compiler.Browser.Results;

namespace NetWasm.Compiler.Browser;

/// <summary>
/// Reuses one compiler and its in-memory frontend artifacts across sequential
/// browser compilations. Instances are not thread-safe.
/// </summary>
public sealed class BrowserCompilerSession : IDisposable
{
    private readonly IDisposable _services;
    private readonly IBrowserCompilationRequestFactory _requests;
    private readonly IBrowserCompilationCommand _compiler;
    private bool _disposed;

    public BrowserCompilerSession() : this(BrowserCompilerCompositionRoot.Create())
    {
    }

    private BrowserCompilerSession(BrowserCompilerComposition composition) : this(
        composition.Services, composition.Requests, composition.Compiler)
    {
    }

    internal BrowserCompilerSession(
        IDisposable services,
        IBrowserCompilationRequestFactory requests,
        IBrowserCompilationCommand compiler)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
    }

    public BrowserCompilationResult Compile(BrowserCompilationRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        using var active = _requests.Begin(request);
        return _compiler.Compile(request);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _services.Dispose();
        _disposed = true;
    }
}
