using System;

namespace NetWasm.Compiler.Browser;

internal interface IBrowserCompilationRequestFactory
{
    IDisposable Begin(BrowserCompilationRequest request);
}

internal interface IBrowserCompilationRequestResolver
{
    BrowserCompilationRequest Resolve();
}

internal sealed class BrowserCompilationRequestState
{
    internal BrowserCompilationRequest? Active { get; set; }
}

internal sealed class BrowserCompilationRequestFactory(BrowserCompilationRequestState state) :
    IBrowserCompilationRequestFactory
{
    public IDisposable Begin(BrowserCompilationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (state.Active is not null)
            throw new InvalidOperationException("The browser compiler session already has an active compilation.");
        state.Active = request;
        return new BrowserCompilationRequestLease(state, request);
    }
}

internal sealed class BrowserCompilationRequestResolver(BrowserCompilationRequestState state) :
    IBrowserCompilationRequestResolver
{
    public BrowserCompilationRequest Resolve() => state.Active ?? throw new InvalidOperationException(
        "Virtual compiler input access requires an active browser compilation.");
}

internal sealed class BrowserCompilationRequestLease(
    BrowserCompilationRequestState state,
    BrowserCompilationRequest request) : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        if (ReferenceEquals(state.Active, request)) state.Active = null;
        _disposed = true;
    }
}
