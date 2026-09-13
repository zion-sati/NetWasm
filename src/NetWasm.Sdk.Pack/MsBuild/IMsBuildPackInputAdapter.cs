using Microsoft.Build.Framework;

namespace NetWasm.Sdk.Pack.MsBuild;

public interface IMsBuildPackInputAdapter
{
    CanonicalPackInputs Adapt(
        string id,
        string version,
        string authors,
        string description,
        string outputPath,
        IReadOnlyList<ITaskItem> files,
        IReadOnlyList<ITaskItem> dependencies,
        string canonicalTargetFramework);
}
