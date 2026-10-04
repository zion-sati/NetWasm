namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeModuleValidator
{
    void Validate(RuntimeNativeModuleValidationRequest request);
}

internal sealed record RuntimeNativeModuleValidationRequest(
    RuntimeNativeValidationProfile Profile,
    string Target,
    string Path,
    string NodePath,
    string CommandPath,
    string ModulePath,
    string LogPath);
