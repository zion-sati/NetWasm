using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.Composition;
using NetWasm.Hosting.Build.Environment;

namespace NetWasm.Hosting.Build.MsBuild;

public sealed class NetWasmResolveBuildEnvironmentTask : Microsoft.Build.Utilities.Task
{
    private readonly IHostingBuildEnvironmentResolver _resolver;

    public NetWasmResolveBuildEnvironmentTask()
        : this(HostingBuildComposition.CreateEnvironmentResolver())
    {
    }

    internal NetWasmResolveBuildEnvironmentTask(IHostingBuildEnvironmentResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    [Required]
    public string ToolchainPackageRoot { get; set; } = string.Empty;
    public string HostToolsPackageRoot { get; set; } = string.Empty;
    public string HostToolsPackageId { get; set; } = string.Empty;
    public string HostToolsPackageVersion { get; set; } = string.Empty;
    public string HostRid { get; set; } = string.Empty;

    [Output] public string NodePath { get; private set; } = string.Empty;
    [Output] public string NodeSource { get; private set; } = string.Empty;
    [Output] public string NodeVersion { get; private set; } = string.Empty;
    [Output] public string WasmToolsCommandPath { get; private set; } = string.Empty;
    [Output] public string WasmToolsModulePath { get; private set; } = string.Empty;
    [Output] public string WasmToolsVersion { get; private set; } = string.Empty;
    [Output] public string WasmLdPath { get; private set; } = string.Empty;
    [Output] public string WasmLdSource { get; private set; } = string.Empty;
    [Output] public string WasmLdVersion { get; private set; } = string.Empty;
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
    [Output] public string NativeBinaryenWasmMergePath { get; private set; } = string.Empty;
    [Output] public string NativeBinaryenWasmMergeVersion { get; private set; } = string.Empty;
    [Output] public string BinaryenWasmOptImplementation { get; private set; } = string.Empty;
    [Output] public string BinaryenWasmMergeImplementation { get; private set; } = string.Empty;
    [Output] public string BinaryenPortableVersion { get; private set; } = string.Empty;
    [Output] public string JcoPath { get; private set; } = string.Empty;
    [Output] public string JcoVersion { get; private set; } = string.Empty;
    [Output] public string Preview2ShimVersion { get; private set; } = string.Empty;
    [Output] public string Preview2ShimRoot { get; private set; } = string.Empty;
    [Output] public string HostingBundleCommandPath { get; private set; } = string.Empty;
    [Output] public string RolldownVersion { get; private set; } = string.Empty;

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
            var result = _resolver.Resolve(request);
            NodePath = result.Node.AbsolutePath;
            NodeSource = result.Node.Source.ToString();
            NodeVersion = result.NodeCompatibility.Version.ToString();
            WasmToolsCommandPath = result.Toolchain.WasmTools.CommandPath;
            WasmToolsModulePath = result.Toolchain.WasmTools.ModulePath;
            WasmToolsVersion = result.Toolchain.WasmTools.WasmToolsVersion;
            WasmLdPath = result.WasmLd.AbsolutePath;
            WasmLdSource = result.WasmLd.Source.ToString();
            WasmLdVersion = result.WasmLdCompatibility.Version.ToString();
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
            NativeBinaryenWasmMergePath =
                result.Binaryen.WasmMerge?.Executable.AbsolutePath ?? string.Empty;
            NativeBinaryenWasmMergeVersion =
                result.Binaryen.WasmMerge?.Compatibility.Version.ToString() ?? string.Empty;
            BinaryenWasmOptImplementation = result.Binaryen.WasmOpt is null
                ? "portable"
                : "native";
            BinaryenWasmMergeImplementation = result.Binaryen.WasmMerge is null
                ? "portable"
                : "native";
            JcoPath = result.Toolchain.Jco.EntryPointPath;
            JcoVersion = result.Toolchain.Jco.JcoVersion;
            Preview2ShimVersion = result.Toolchain.Jco.Preview2ShimVersion;
            Preview2ShimRoot = result.Toolchain.Preview2ShimRoot;
            HostingBundleCommandPath = result.Toolchain.HostingBundle.CommandPath;
            RolldownVersion = result.Toolchain.HostingBundle.RolldownVersion;
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK040: {exception.Message}");
            return false;
        }
    }
}
