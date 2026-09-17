namespace NetWasm.Compiler.Browser.Tests;

public sealed class BrowserCompilationRequestSessionTests
{
    [Fact]
    public void ActivatesExactlyOneRequestAndReleasesItIdempotently()
    {
        var state = new BrowserCompilationRequestState();
        var factory = new BrowserCompilationRequestFactory(state);
        var resolver = new BrowserCompilationRequestResolver(state);
        var request = Request();

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve());
        Assert.Throws<ArgumentNullException>(() => factory.Begin(null!));
        var active = factory.Begin(request);
        Assert.Same(request, resolver.Resolve());
        Assert.Throws<InvalidOperationException>(() => factory.Begin(Request()));

        active.Dispose();
        active.Dispose();
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve());
    }

    [Fact]
    public void AStaleLeaseCannotClearANewerRequest()
    {
        var state = new BrowserCompilationRequestState();
        var staleRequest = Request();
        var currentRequest = Request();
        var stale = new BrowserCompilationRequestLease(state, staleRequest);
        state.Active = currentRequest;

        stale.Dispose();

        Assert.Same(currentRequest, state.Active);
    }

    private static BrowserCompilationRequest Request() => new(
        BrowserCompilationRequestTests.CreateOptions(),
        new Dictionary<string, byte[]>(), new Dictionary<string, string>());
}
