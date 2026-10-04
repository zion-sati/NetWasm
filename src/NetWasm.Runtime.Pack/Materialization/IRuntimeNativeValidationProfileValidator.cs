namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeValidationProfileValidator
{
    void Validate(RuntimeNativeValidationProfile profile, string target);
}
