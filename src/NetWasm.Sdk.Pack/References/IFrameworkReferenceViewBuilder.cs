namespace NetWasm.Sdk.Pack.References;

public sealed record FrameworkReferenceViewRequest(
    byte[] Implementation,
    byte[] Platform,
    IReadOnlyList<string> ForwardedTypes);

/// <summary>Produces a reference view; never changes the supplied implementation image.</summary>
public interface IFrameworkReferenceViewBuilder
{
    byte[] Build(FrameworkReferenceViewRequest request);
}
