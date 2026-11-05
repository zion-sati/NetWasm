using System.Collections.Immutable;
using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.Composition;
using NetWasm.Hosting.Build.Environment;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Hosting.Build.MsBuild;

public sealed class NetWasmResolveBuildEnvironmentTask : Microsoft.Build.Utilities.Task
{
    private readonly IHostingBuildEnvironmentResolver _resolver;
    private readonly IBuildEnvironmentStamper _stamper;
    private readonly IBuildEnvironmentCacheReader _cacheReader;
    private readonly IBuildEnvironmentCacheWriter _cacheWriter;

    public NetWasmResolveBuildEnvironmentTask()
        : this(
            HostingBuildComposition.CreateEnvironmentResolver(),
            new BuildEnvironmentStamper(new SystemHostEnvironmentVariableReader()),
            new BuildEnvironmentCacheReader(),
            new BuildEnvironmentCacheWriter())
    {
    }

    internal NetWasmResolveBuildEnvironmentTask(IHostingBuildEnvironmentResolver resolver)
        : this(resolver, new BuildEnvironmentStamper(new SystemHostEnvironmentVariableReader()),
            new BuildEnvironmentCacheReader(), new BuildEnvironmentCacheWriter())
    {
    }

    internal NetWasmResolveBuildEnvironmentTask(
        IHostingBuildEnvironmentResolver resolver,
        IBuildEnvironmentStamper stamper,
        IBuildEnvironmentCacheReader cacheReader,
        IBuildEnvironmentCacheWriter cacheWriter)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _stamper = stamper ?? throw new ArgumentNullException(nameof(stamper));
        _cacheReader = cacheReader ?? throw new ArgumentNullException(nameof(cacheReader));
        _cacheWriter = cacheWriter ?? throw new ArgumentNullException(nameof(cacheWriter));
    }

    [Required]
    public string ToolchainPackageRoot { get; set; } = string.Empty;
    public string HostToolsPackageRoot { get; set; } = string.Empty;
    public string HostToolsPackageId { get; set; } = string.Empty;
    public string HostToolsPackageVersion { get; set; } = string.Empty;
    public string HostRid { get; set; } = string.Empty;
    // Where to keep the resolved outputs between builds. Resolving verifies every
    // restored package file, which the stamp of their sizes and write times replaces.
    public string CachePath { get; set; } = string.Empty;
    // A directory of outputs by stamp, in place of CachePath. The outputs depend only on
    // what the stamp covers, so every project, and every clean build, shares them.
    public string CacheDirectory { get; set; } = string.Empty;

    [Output] public string NodePath { get; private set; } = string.Empty;
    [Output] public string NodeSource { get; private set; } = string.Empty;
    [Output] public string NodeVersion { get; private set; } = string.Empty;
    [Output] public string WasmToolsCommandPath { get; private set; } = string.Empty;
    [Output] public string WasmToolsModulePath { get; private set; } = string.Empty;
    [Output] public string WasmToolsVersion { get; private set; } = string.Empty;
    // A native wasm-tools of that version, which the build runs in place of the WASI one.
    [Output] public string NativeWasmToolsPath { get; private set; } = string.Empty;
    [Output] public string WasmLdPath { get; private set; } = string.Empty;
    [Output] public string WasmLdSource { get; private set; } = string.Empty;
    [Output] public string WasmLdVersion { get; private set; } = string.Empty;
    // nwc, the NetWasm compiler, when the restored host tools carry it.
    [Output] public string CompilerPath { get; private set; } = string.Empty;
    [Output] public string ToolchainPackageId { get; private set; } = string.Empty;
    [Output] public string ToolchainPackageVersion { get; private set; } = string.Empty;
    [Output] public string CommandWitPackagePath { get; private set; } = string.Empty;
    [Output] public string AsyncCommandWitPackagePath { get; private set; } = string.Empty;
    [Output] public string CompilerWitPackagePath { get; private set; } = string.Empty;
    [Output] public string RawInspectionCommandPath { get; private set; } = string.Empty;
    [Output] public string BinaryenPath { get; private set; } = string.Empty;
    [Output] public string BinaryenWasmOptPath { get; private set; } = string.Empty;
    [Output] public string BinaryenWasmMergePath { get; private set; } = string.Empty;
    [Output] public string NativeBinaryenWasmOptPath { get; private set; } = string.Empty;
    [Output] public string NativeBinaryenWasmOptVersion { get; private set; } = string.Empty;
    [Output] public string BinaryenWasmOptImplementation { get; private set; } = string.Empty;
    [Output] public string BinaryenPortableVersion { get; private set; } = string.Empty;
    [Output] public string JcoPath { get; private set; } = string.Empty;
    [Output] public string JcoVersion { get; private set; } = string.Empty;
    [Output] public string Preview2ShimVersion { get; private set; } = string.Empty;
    [Output] public string Preview2ShimRoot { get; private set; } = string.Empty;
    [Output] public string TimeZoneSourceRoot { get; private set; } = string.Empty;
    [Output] public string TimeZoneDataVersion { get; private set; } = string.Empty;
    [Output] public string HostingBundleCommandPath { get; private set; } = string.Empty;
    [Output] public string RolldownVersion { get; private set; } = string.Empty;

    private static readonly ImmutableArray<(string Name, Func<NetWasmResolveBuildEnvironmentTask, string> Get, Action<NetWasmResolveBuildEnvironmentTask, string> Set)> Outputs =
    [
        (nameof(NodePath), task => task.NodePath, (task, value) => task.NodePath = value),
        (nameof(NodeSource), task => task.NodeSource, (task, value) => task.NodeSource = value),
        (nameof(NodeVersion), task => task.NodeVersion, (task, value) => task.NodeVersion = value),
        (nameof(WasmToolsCommandPath), task => task.WasmToolsCommandPath, (task, value) => task.WasmToolsCommandPath = value),
        (nameof(WasmToolsModulePath), task => task.WasmToolsModulePath, (task, value) => task.WasmToolsModulePath = value),
        (nameof(WasmToolsVersion), task => task.WasmToolsVersion, (task, value) => task.WasmToolsVersion = value),
        (nameof(NativeWasmToolsPath), task => task.NativeWasmToolsPath, (task, value) => task.NativeWasmToolsPath = value),
        (nameof(WasmLdPath), task => task.WasmLdPath, (task, value) => task.WasmLdPath = value),
        (nameof(WasmLdSource), task => task.WasmLdSource, (task, value) => task.WasmLdSource = value),
        (nameof(WasmLdVersion), task => task.WasmLdVersion, (task, value) => task.WasmLdVersion = value),
        (nameof(CompilerPath), task => task.CompilerPath, (task, value) => task.CompilerPath = value),
        (nameof(ToolchainPackageId), task => task.ToolchainPackageId, (task, value) => task.ToolchainPackageId = value),
        (nameof(ToolchainPackageVersion), task => task.ToolchainPackageVersion, (task, value) => task.ToolchainPackageVersion = value),
        (nameof(CommandWitPackagePath), task => task.CommandWitPackagePath, (task, value) => task.CommandWitPackagePath = value),
        (nameof(AsyncCommandWitPackagePath), task => task.AsyncCommandWitPackagePath, (task, value) => task.AsyncCommandWitPackagePath = value),
        (nameof(CompilerWitPackagePath), task => task.CompilerWitPackagePath, (task, value) => task.CompilerWitPackagePath = value),
        (nameof(RawInspectionCommandPath), task => task.RawInspectionCommandPath, (task, value) => task.RawInspectionCommandPath = value),
        (nameof(BinaryenPath), task => task.BinaryenPath, (task, value) => task.BinaryenPath = value),
        (nameof(BinaryenWasmOptPath), task => task.BinaryenWasmOptPath, (task, value) => task.BinaryenWasmOptPath = value),
        (nameof(BinaryenWasmMergePath), task => task.BinaryenWasmMergePath, (task, value) => task.BinaryenWasmMergePath = value),
        (nameof(NativeBinaryenWasmOptPath), task => task.NativeBinaryenWasmOptPath, (task, value) => task.NativeBinaryenWasmOptPath = value),
        (nameof(NativeBinaryenWasmOptVersion), task => task.NativeBinaryenWasmOptVersion, (task, value) => task.NativeBinaryenWasmOptVersion = value),
        (nameof(BinaryenWasmOptImplementation), task => task.BinaryenWasmOptImplementation, (task, value) => task.BinaryenWasmOptImplementation = value),
        (nameof(BinaryenPortableVersion), task => task.BinaryenPortableVersion, (task, value) => task.BinaryenPortableVersion = value),
        (nameof(JcoPath), task => task.JcoPath, (task, value) => task.JcoPath = value),
        (nameof(JcoVersion), task => task.JcoVersion, (task, value) => task.JcoVersion = value),
        (nameof(Preview2ShimVersion), task => task.Preview2ShimVersion, (task, value) => task.Preview2ShimVersion = value),
        (nameof(Preview2ShimRoot), task => task.Preview2ShimRoot, (task, value) => task.Preview2ShimRoot = value),
        (nameof(TimeZoneSourceRoot), task => task.TimeZoneSourceRoot, (task, value) => task.TimeZoneSourceRoot = value),
        (nameof(TimeZoneDataVersion), task => task.TimeZoneDataVersion, (task, value) => task.TimeZoneDataVersion = value),
        (nameof(HostingBundleCommandPath), task => task.HostingBundleCommandPath, (task, value) => task.HostingBundleCommandPath = value),
        (nameof(RolldownVersion), task => task.RolldownVersion, (task, value) => task.RolldownVersion = value),
    ];

    public override bool Execute()
    {
        try
        {
            var request = string.IsNullOrWhiteSpace(HostToolsPackageRoot)
                ? new HostingBuildEnvironmentRequest(ToolchainPackageRoot)
                : new HostingBuildEnvironmentRequest(
                    ToolchainPackageRoot,
                    HostToolsPackageRoot,
                    HostToolsPackageId,
                    HostToolsPackageVersion,
                    HostRid);
            var stamp = string.IsNullOrWhiteSpace(CachePath) && string.IsNullOrWhiteSpace(CacheDirectory)
                ? null
                : _stamper.Stamp(request);
            var cachePath = stamp is null ? null
                : string.IsNullOrWhiteSpace(CacheDirectory) ? CachePath
                : Path.Combine(CacheDirectory, stamp + ".json");
            if (cachePath is not null && _cacheReader.Read(cachePath, stamp!) is { } cached &&
                Outputs.All(output => cached.ContainsKey(output.Name)))
            {
                foreach (var output in Outputs)
                {
                    output.Set(this, cached[output.Name]);
                }
                return true;
            }
            var result = _resolver.Resolve(request);
            NodePath = result.Node.AbsolutePath;
            NodeSource = result.Node.Source.ToString();
            NodeVersion = result.NodeCompatibility.Version.ToString();
            WasmToolsCommandPath = result.Toolchain.WasmTools.CommandPath;
            WasmToolsModulePath = result.Toolchain.WasmTools.ModulePath;
            WasmToolsVersion = result.Toolchain.WasmTools.WasmToolsVersion;
            NativeWasmToolsPath = result.NativeWasmTools?.Executable.AbsolutePath ?? string.Empty;
            WasmLdPath = result.WasmLd.AbsolutePath;
            WasmLdSource = result.WasmLd.Source.ToString();
            WasmLdVersion = result.WasmLdCompatibility.Version.ToString();
            CompilerPath = result.CompilerPath ?? string.Empty;
            ToolchainPackageId = result.Toolchain.PackageId;
            ToolchainPackageVersion = result.Toolchain.PackageVersion;
            CommandWitPackagePath = result.Toolchain.WitPackages.CommandPath;
            AsyncCommandWitPackagePath = result.Toolchain.WitPackages.AsyncCommandPath;
            CompilerWitPackagePath = result.Toolchain.WitPackages.CompilerPath;
            RawInspectionCommandPath = result.Toolchain.RawInspection.InspectionCommandPath;
            BinaryenPath = result.Toolchain.RawInspection.BinaryenModulePath;
            BinaryenWasmOptPath = result.Toolchain.RawInspection.WasmOptPath;
            BinaryenWasmMergePath = result.Toolchain.RawInspection.WasmMergePath;
            BinaryenPortableVersion = result.Toolchain.RawInspection.BinaryenVersion;
            NativeBinaryenWasmOptPath =
                result.Binaryen.WasmOpt?.Executable.AbsolutePath ?? string.Empty;
            NativeBinaryenWasmOptVersion =
                result.Binaryen.WasmOpt?.Compatibility.Version.ToString() ?? string.Empty;
            BinaryenWasmOptImplementation = result.Binaryen.WasmOpt is null
                ? "portable"
                : "native";
            JcoPath = result.Toolchain.Jco.EntryPointPath;
            JcoVersion = result.Toolchain.Jco.JcoVersion;
            Preview2ShimVersion = result.Toolchain.Jco.Preview2ShimVersion;
            Preview2ShimRoot = result.Toolchain.Preview2ShimRoot;
            TimeZoneSourceRoot = result.Toolchain.TimeZones.SourceRoot;
            TimeZoneDataVersion = result.Toolchain.TimeZones.DataVersion;
            HostingBundleCommandPath = result.Toolchain.HostingBundle.CommandPath;
            RolldownVersion = result.Toolchain.HostingBundle.RolldownVersion;
            if (cachePath is not null)
            {
                _cacheWriter.Write(cachePath, stamp!,
                    Outputs.ToDictionary(output => output.Name, output => output.Get(this), StringComparer.Ordinal));
            }
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK040: {exception.Message}");
            return false;
        }
    }
}
