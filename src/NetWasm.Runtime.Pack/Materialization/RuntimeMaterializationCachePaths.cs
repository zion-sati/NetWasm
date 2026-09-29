using System;
using System.IO;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

internal static class RuntimeMaterializationCachePaths
{
    public static string Entry(
        string cacheDirectory,
        RuntimeMaterializationCacheSlot slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(slot);
        if (string.IsNullOrWhiteSpace(slot.Target) ||
            !slot.Target.All(character => char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.') ||
            !Enum.IsDefined(slot.Optimization))
        {
            throw new InvalidOperationException("The NetWasm runtime cache slot is invalid.");
        }

        return Path.Combine(
            Path.GetFullPath(cacheDirectory),
            "runtime-materialization",
            "v2",
            slot.Target,
            $"{slot.Optimization}.nwcache");
    }
}
