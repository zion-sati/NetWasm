using System.Globalization;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record RandomCilRunDirectory(string Root, string Directory);

internal interface IRandomCilRunDirectoryFactory
{
    RandomCilRunDirectory Create(CilProfile profile, int shardIndex, int sliceIndex, bool captureDiagnostics);
}

internal sealed class RandomCilRunDirectoryFactory(ICorpusRunDirectoryFactory directories) : IRandomCilRunDirectoryFactory
{
    public RandomCilRunDirectory Create(CilProfile profile, int shardIndex, int sliceIndex, bool captureDiagnostics)
    {
        if (!CilProfiles.Roslyn.Contains(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(shardIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(sliceIndex);
        var root = directories.Create();
        return new(root, Path.Combine(root, profile.ToString().ToLowerInvariant(),
            "shard-" + shardIndex.ToString("D4", CultureInfo.InvariantCulture),
            "slice-" + sliceIndex.ToString("D2", CultureInfo.InvariantCulture),
            captureDiagnostics ? "diagnostic" : "normal"));
    }
}
