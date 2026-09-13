using System;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerRootMapSnapshotBuilder : ICompilerRootMapSnapshotBuilder
{
    public object BuildRootMaps(ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return new
        {
            Direct = program.RootMaps.OrderBy(
                    pair => pair.Key.Assembly.Name,
                    StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.MetadataToken)
                .Select(pair => BuildRootMap(pair.Key.ToString(), pair.Value)),
            Constructed = program.ConstructedRootMaps.OrderBy(
                    pair => pair.Key,
                    StringComparer.Ordinal)
                .Select(pair => BuildRootMap(pair.Key, pair.Value)),
        };
    }

    private static object BuildRootMap(string identity, MethodRootMap map) => new
    {
        Identity = identity,
        Method = map.Method.ToString(),
        Slots = map.Slots.OrderBy(pair => pair.Value).Select(pair => new
        {
            Slot = pair.Value,
            Source = pair.Key.ToString(),
        }),
        Safepoints = map.Safepoints.OrderBy(pair => pair.Key).Select(pair => new
        {
            Offset = pair.Key,
            Roots = pair.Value.Roots.Select(root => root.ToString()),
            ConstructorRoots = pair.Value.ConstructorCallRoots
                .Select(root => root.ToString()),
        }),
    };
}
