namespace NetWasm.Sdk.Pack.MsBuild;

public interface IProjectPackageIdentityResolver
{
    ProjectPackageIdentity? Resolve(string projectPath, ProjectEvaluationRequest request);
}
