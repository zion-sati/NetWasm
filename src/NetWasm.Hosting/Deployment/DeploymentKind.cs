namespace NetWasm.Hosting.Deployment;

/// <summary>The form of the immutable artifact set described by a deployment manifest.</summary>
public enum DeploymentKind
{
    Component,
    Raw,
    Browser,
}
