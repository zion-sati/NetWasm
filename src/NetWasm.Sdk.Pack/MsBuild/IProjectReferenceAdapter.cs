using Microsoft.Build.Framework;

namespace NetWasm.Sdk.Pack.MsBuild;

public interface IProjectReferenceAdapter
{
    CanonicalPackageDependencyInput? Adapt(ITaskItem item, string canonicalTargetFramework);
}
