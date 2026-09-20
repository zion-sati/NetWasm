using System;
using System.Collections.Immutable;

namespace NetWasm.Toolchain.Prerequisites;

public sealed record HostToolsPackageRequest(
    string PackageRoot,
    string PackageId,
    string PackageVersion,
    string HostRid);

public sealed record HostToolsPackageFile(string RelativePath, long Size, string Sha256);

public sealed record HostToolsPackageManifest(
    string PackageId,
    string PackageVersion,
    string HostRid,
    string NodeVersion,
    string WasmLdVersion,
    string BinaryenVersion,
    ImmutableDictionary<string, string> Roles,
    ImmutableArray<HostToolsPackageFile> Files);

public sealed record HostToolsPackageFileContents(
    byte[] Bytes,
    bool IsSymbolicLink,
    bool IsExecutable);

public sealed record HostToolsPackagePaths(
    string PackageId,
    string PackageVersion,
    string HostRid,
    string NodeVersion,
    string WasmLdVersion,
    string BinaryenVersion,
    ImmutableDictionary<string, string> Roles);

public interface IHostToolsPackageFileReader
{
    HostToolsPackageFileContents Read(string absolutePath);
}

public interface IHostToolsPackageManifestReader
{
    HostToolsPackageManifest Read(ReadOnlyMemory<byte> json);
}

public interface IHostToolsPackageResolver
{
    HostToolsPackagePaths Resolve(HostToolsPackageRequest request);
}
