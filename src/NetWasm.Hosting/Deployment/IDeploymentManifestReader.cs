using System;

namespace NetWasm.Hosting.Deployment;

public interface IDeploymentManifestReader
{
    DeploymentManifest Read(ReadOnlyMemory<byte> utf8Json);
}
