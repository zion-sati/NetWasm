using System.Collections.Immutable;

namespace NetWasm.Compiler.Browser.Tests;

internal sealed class FixedBrowserCompilationRequestResolver(BrowserCompilationRequest request) :
    IBrowserCompilationRequestResolver
{
    private readonly BrowserCompilationRequest _request = request ??
        throw new ArgumentNullException(nameof(request));

    public BrowserCompilationRequest Resolve() => _request;

    internal static FixedBrowserCompilationRequestResolver ForInputs(
        ImmutableDictionary<string, ImmutableArray<byte>> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return new(new(BrowserCompilationRequestTests.CreateOptions(),
            inputs.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToArray()),
            new Dictionary<string, string>()));
    }

    internal static FixedBrowserCompilationRequestResolver ForDocuments(
        ImmutableDictionary<string, string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return new(new(BrowserCompilationRequestTests.CreateOptions(),
            documents.Keys.ToDictionary(static path => path, static _ => Array.Empty<byte>()),
            documents));
    }
}
