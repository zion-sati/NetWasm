using NetWasm.Toolchain.Manifest;

namespace NetWasm.Toolchain.Resolution;

public interface IPinnedPlatformAssetPathResolver
{
    ResolvedPlatformAsset Resolve(string assetId);
}
