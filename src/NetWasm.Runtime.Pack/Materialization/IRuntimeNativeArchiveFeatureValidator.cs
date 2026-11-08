namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeArchiveFeatureValidator
{
    void Validate(RuntimeNativeArchiveFeatureValidationRequest request);
}

// One static native library's members, linked on their own, validated against the profile.
internal sealed record RuntimeNativeArchiveFeatureValidationRequest(string LibraryName, RuntimeNativeModuleValidationRequest Module);
