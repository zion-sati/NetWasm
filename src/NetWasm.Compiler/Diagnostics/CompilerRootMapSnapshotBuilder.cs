using System;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerRootMapSnapshotBuilder : ICompilerRootMapSnapshotBuilder
{
    public CompilerRootMapsArtifact BuildRootMaps(ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return new CompilerRootMapsArtifact(
            [.. program.RootMaps.OrderBy(
                    pair => pair.Key.Assembly.Name,
                    StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.MetadataToken)
                .Select(pair => BuildRootMap(pair.Key.ToString(), pair.Value))],
            [.. program.ConstructedRootMaps.OrderBy(
                    pair => pair.Key,
                    StringComparer.Ordinal)
                .Select(pair => BuildRootMap(pair.Key, pair.Value))]);
    }

    private static CompilerRootMapArtifact BuildRootMap(string identity, MethodRootMap map) => new(
        identity,
        map.Method.ToString(),
        [.. map.Slots.OrderBy(pair => pair.Value)
            .Select(pair => new CompilerRootSlotArtifact(pair.Value, pair.Key.ToString()))],
        [.. map.Safepoints.OrderBy(pair => pair.Key).Select(pair => new CompilerSafepointArtifact(
            pair.Key,
            [.. pair.Value.Roots.Select(root => root.ToString())],
            [.. pair.Value.ConstructorCallRoots.Select(root => root.ToString())]))]);
}
