namespace NetWasm.Hosting.Deployment;

public interface IDeploymentFreshnessValidator
{
    DeploymentManifest Validate(DeploymentFreshnessRequest request);
}
