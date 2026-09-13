using System;
using System.Globalization;

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
                ? $"{Prefix(target)}|_ex_{VersionedInterface(function.InterfaceName)}"
                : ImportModule(function.InterfaceName, target);

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
        ExportCore(interfaceName, ExportFunctionName(functionName), target);

    public static string Export(CanonicalAbiFunction function, WasmTarget target) =>
        ExportCore(
            function.InterfaceName,
            function.Kind == CanonicalAbiFunctionKind.ExportedResourceDestructor
                ? $"{function.ResourceName}_dtor"
                : function.FunctionName,
            target);

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

    private static string ExportCore(
        string interfaceName,
        string functionName,
        WasmTarget target) => interfaceName.Length == 0
            ? $"{Prefix(target)}||{functionName}"
            : $"{Prefix(target)}|{VersionedInterface(interfaceName)}|{functionName}";

    private static string ExportFunctionName(string functionName)
    {
        const string prefix = "[resource-dtor]";
        return functionName.StartsWith(prefix, StringComparison.Ordinal)
            ? functionName[prefix.Length..] + "_dtor"
            : functionName;
    }

    private static string VersionedInterface(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var separator = value.LastIndexOf('/');
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new ArgumentException(
                "WIT interface name must use 'namespace:package@version/interface' syntax",
                nameof(value));
        }
        var package = value[..separator];
        var interfaceName = value[(separator + 1)..];
        var versionSeparator = package.LastIndexOf('@');
        if (versionSeparator <= 0 || versionSeparator == package.Length - 1)
        {
            throw new ArgumentException(
                "WIT interface package must contain a semantic version",
                nameof(value));
        }
        var version = package[(versionSeparator + 1)..];
        var compatibilityVersion = CompatibilityVersion(version);
        return $"{package[..versionSeparator]}/{interfaceName}@{compatibilityVersion}";
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
