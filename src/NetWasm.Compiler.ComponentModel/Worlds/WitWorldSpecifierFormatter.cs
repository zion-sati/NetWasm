using System;
using System.Collections.Frozen;
using System.Linq;

namespace NetWasm.Compiler.ComponentModel.Worlds;

public sealed class WitWorldSpecifierFormatter : IWitWorldSpecifierFormatter
{
    // wit-parser's keywords: a name that is one is written with a leading '%'.
    private static readonly FrozenSet<string> Keywords = new[]
    {
        "use", "type", "func", "u8", "u16", "u32", "u64", "s8", "s16", "s32", "s64", "f32", "f64",
        "char", "resource", "own", "borrow", "record", "flags", "variant", "enum", "bool", "string",
        "option", "result", "future", "stream", "error-context", "list", "map", "as", "from",
        "static", "interface", "tuple", "world", "import", "export", "package", "constructor",
        "include", "with", "async",
    }.ToFrozenSet(StringComparer.Ordinal);

    public string Format(WitWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var versionIndex = world.Package.LastIndexOf('@');
        var package = versionIndex < 0 ? world.Package : world.Package[..versionIndex];
        var version = versionIndex < 0 ? string.Empty : world.Package[versionIndex..];
        return $"{string.Join(':', package.Split(':').Select(Escape))}/{Escape(world.Name)}{version}";
    }

    private static string Escape(string name) => Keywords.Contains(name) ? "%" + name : name;
}
