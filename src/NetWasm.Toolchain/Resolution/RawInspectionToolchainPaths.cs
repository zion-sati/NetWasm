using System;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Resolution;

public sealed record RawInspectionToolchainPaths(
    string PackageId,
    string PackageVersion,
    string NodePath,
    Version NodeVersion,
    string InspectionCommandPath,
    string BinaryenModulePath,
    string WasmOptPath,
    string WasmMergePath);

public interface IRawInspectionToolchainPathResolver
{
    RawInspectionToolchainPaths Resolve(
        ResolvedHostExecutable nodeExecutable,
        ValidatedHostToolCompatibility nodeCompatibility);
}
