namespace NetWasm.Sdk.Pack.Packing;

public interface ILinkTargetResolver
{
    string? Resolve(string path);
}
