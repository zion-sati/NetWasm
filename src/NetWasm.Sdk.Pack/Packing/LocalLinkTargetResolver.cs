namespace NetWasm.Sdk.Pack.Packing;

public sealed class LocalLinkTargetResolver : ILinkTargetResolver
{
    public string? Resolve(string path) => new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
}
