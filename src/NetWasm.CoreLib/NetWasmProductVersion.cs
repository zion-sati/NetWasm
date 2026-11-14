namespace System;

/// <summary>NetWasm's product version, which Environment.Version and RuntimeInformation report.</summary>
/// <remarks>
/// The project build generates the constant from eng/NetWasm.ReleaseVersion.txt (the release
/// tooling projects the release version into that file) and defines
/// NETWASM_PRODUCT_VERSION_GENERATED. Sources compiled without the project, such as test CoreLib
/// variants, use the fallback below.
/// </remarks>
internal static partial class NetWasmProductVersion
{
#if !NETWASM_PRODUCT_VERSION_GENERATED
    internal const string Value = "0.0.0";
#endif
}
