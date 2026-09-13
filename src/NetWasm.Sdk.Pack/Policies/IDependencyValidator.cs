namespace NetWasm.Sdk.Pack.Policies;

public interface IDependencyValidator
{
    CanonicalPackageDependency Validate(CanonicalPackageDependencyInput dependency, TargetProfile profile);
}
