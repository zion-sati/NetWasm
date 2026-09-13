namespace NetWasm.Hosting.Deployment;

public interface IDeploymentManifestValidator
{
    void Validate(DeploymentManifest manifest);
}
