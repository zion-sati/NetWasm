using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeValidationArgumentBuilder
{
    ImmutableArray<string> Build(RuntimeNativeValidationProfile profile, string target, string path);
}
