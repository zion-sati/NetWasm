using System;
using System.Globalization;
using System.Linq;

namespace NetWasm.Compiler.Core;

public static class CanonicalAbiNames
{
    public static string ImportModule(string interfaceName, WasmTarget target) =>
        interfaceName.Length == 0
            ? Prefix(target)
            : $"{Prefix(target)}|{VersionedInterface(interfaceName)}";

    public static string ImportModule(
        CanonicalAbiFunction function,
        WasmTarget target) => function.Kind is
            CanonicalAbiFunctionKind.ExportedResourceNew or
            CanonicalAbiFunctionKind.ExportedResourceRep or
            CanonicalAbiFunctionKind.ExportedResourceDrop
                ? $"{Prefix(target)}|_ex_{PhysicalInterfaceName(function)}"
                : ImportCoreModule(PhysicalInterfaceName(function), target);

    public static string ImportName(CanonicalAbiFunction function) =>
        function.Kind switch
        {
            CanonicalAbiFunctionKind.ImportedResourceDrop =>
                $"{function.ResourceName}_drop",
            CanonicalAbiFunctionKind.ExportedResourceNew =>
                $"{function.ResourceName}_new",
            CanonicalAbiFunctionKind.ExportedResourceRep =>
                $"{function.ResourceName}_rep",
            CanonicalAbiFunctionKind.ExportedResourceDrop =>
                $"{function.ResourceName}_drop",
            _ => function.FunctionName,
        };

    public static string Export(string interfaceName, string functionName, WasmTarget target) =>
        ExportCore(
            interfaceName.Length == 0 ? string.Empty : VersionedInterface(interfaceName),
            ExportFunctionName(functionName),
            target);

    public static string Export(CanonicalAbiFunction function, WasmTarget target) =>
        ExportCore(
            PhysicalInterfaceName(function),
            function.Kind == CanonicalAbiFunctionKind.ExportedResourceDestructor
                ? $"{function.ResourceName}_dtor"
                : function.FunctionName,
            target);

    public static string PostReturn(
        CanonicalAbiFunction function,
        WasmTarget target) => $"{Export(function, target)}_post";

    public static string CoreInterfaceName(string interfaceName) =>
        VersionedInterface(interfaceName);

    public static string PostReturn(
        string interfaceName,
        string functionName,
        WasmTarget target) => $"{Export(interfaceName, functionName, target)}_post";

    public static string Memory(WasmTarget target) => $"{Prefix(target)}_memory";

    public static string Reallocate(WasmTarget target) => $"{Prefix(target)}_realloc";

    public static string Initialize(WasmTarget target) => $"{Prefix(target)}_initialize";

    public static string ModulePrefix(WasmTarget target) => Prefix(target);

    private static string Prefix(WasmTarget target) => target switch
    {
        WasmTarget.Wasm32 => "cm32p2",
        WasmTarget.Wasm64 => "cm64p2",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    private static string ImportCoreModule(string coreInterfaceName, WasmTarget target) =>
        coreInterfaceName.Length == 0
            ? Prefix(target)
            : $"{Prefix(target)}|{coreInterfaceName}";

    private static string PhysicalInterfaceName(CanonicalAbiFunction function) =>
        string.Equals(function.CoreInterfaceName, function.InterfaceName,
            StringComparison.Ordinal)
            ? SemanticInterfaceName(function.InterfaceName)
            : ValidatePhysicalInterfaceName(function);

    private static string ValidatePhysicalInterfaceName(
        CanonicalAbiFunction function)
    {
        _ = SemanticInterfaceName(function.InterfaceName);
        return function.CoreInterfaceName;
    }

    private static string SemanticInterfaceName(string interfaceName) =>
        interfaceName.Length == 0
            ? string.Empty
            : VersionedInterface(interfaceName);

    private static string ExportCore(
        string coreInterfaceName,
        string functionName,
        WasmTarget target) => coreInterfaceName.Length == 0
            ? $"{Prefix(target)}||{functionName}"
            : $"{Prefix(target)}|{coreInterfaceName}|{functionName}";

    private static string ExportFunctionName(string functionName)
    {
        const string prefix = "[resource-dtor]";
        return functionName.StartsWith(prefix, StringComparison.Ordinal)
            ? functionName[prefix.Length..] + "_dtor"
            : functionName;
    }

    // The core name of an interface from its world key: `namespace:package/name`
    // with the version, if any, as its compatibility version (1 for 1.2.3, 0.2
    // for 0.2.3), or the plain name of an interface the world declares inline.
    private static string VersionedInterface(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var separator = value.IndexOf('/');
        if (separator < 0)
        {
            return IsWitName(value)
                ? value
                : throw new ArgumentException(
                    $"WIT interface key '{value}' is not a WIT name or 'namespace:package/interface@version'",
                    nameof(value));
        }
        var package = value[..separator];
        var rest = value[(separator + 1)..];
        var versionSeparator = rest.IndexOf('@');
        var interfaceName = versionSeparator < 0 ? rest : rest[..versionSeparator];
        var colon = package.IndexOf(':');
        if (colon <= 0 || package.Contains('@', StringComparison.Ordinal) || !IsWitName(interfaceName) ||
            versionSeparator == rest.Length - 1)
        {
            throw new ArgumentException(
                $"WIT interface key '{value}' must use 'namespace:package/interface@version' syntax, " +
                "as the WIT world names it; regenerate bindings made with an older netwasm-wit-bindgen",
                nameof(value));
        }
        return versionSeparator < 0
            ? value
            : $"{package}/{interfaceName}@{CompatibilityVersion(rest[(versionSeparator + 1)..])}";
    }

    // A WIT identifier: lowercase kebab-case words, optionally %-escaped.
    private static bool IsWitName(string value)
    {
        var name = value.StartsWith('%') ? value[1..] : value;
        return name.Length > 0 && name[0] != '-' && name[^1] != '-' &&
            !name.Contains("--", StringComparison.Ordinal) &&
            name.All(static character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-');
    }

    private static string CompatibilityVersion(string version)
    {
        var metadataSeparator = version.IndexOfAny(['-', '+']);
        var core = metadataSeparator < 0 ? version : version[..metadataSeparator];
        var parts = core.Split('.');
        if (!uint.TryParse(parts[0], out var major))
        {
            throw new ArgumentException("WIT interface package version is not semantic");
        }
        if (major != 0)
        {
            return major.ToString(CultureInfo.InvariantCulture);
        }
        if (parts.Length < 2 || !uint.TryParse(parts[1], out var minor))
        {
            throw new ArgumentException(
                "pre-1.0 WIT interface package version must include a minor version");
        }
        return "0." + minor.ToString(CultureInfo.InvariantCulture);
    }
}
