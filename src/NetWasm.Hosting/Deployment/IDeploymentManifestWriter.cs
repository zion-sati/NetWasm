namespace NetWasm.Hosting.Deployment;

public interface IDeploymentManifestWriter
{
    byte[] Write(DeploymentManifest manifest);
}
