using System.Collections.Immutable;

namespace NetWasm.Toolchain.Manifest;

public static class ToolchainPlatformAssetIds
{
    public const string RawInspectionCommand = "netwasm.raw-inspection.command";
    public const string RawInspectionCommandCore = "netwasm.raw-inspection.command-core";
    public const string RawInspectionError = "netwasm.raw-inspection.error";
    public const string RawImportInspector = "netwasm.raw-inspection.inspector";
    public const string CoreImportReader = "netwasm.raw-inspection.core-reader";
    public const string BinaryenImportReader = "netwasm.raw-inspection.binaryen-reader";
    public const string BinaryenLicense = "binaryen.license";
    public const string BinaryenReadme = "binaryen.readme";
    public const string BinaryenPackage = "binaryen.package";
    public const string BinaryenModule = "binaryen.module";
    public const string BinaryenTypes = "binaryen.types";
    public const string BinaryenBinPackage = "binaryen.bin-package";
    public const string BinaryenWasmAs = "binaryen.wasm-as";
    public const string BinaryenWasmCtorEval = "binaryen.wasm-ctor-eval";
    public const string BinaryenWasmDis = "binaryen.wasm-dis";
    public const string BinaryenWasmMerge = "binaryen.wasm-merge";
    public const string BinaryenWasmMetadce = "binaryen.wasm-metadce";
    public const string BinaryenWasmOpt = "binaryen.wasm-opt";
    public const string BinaryenWasmReduce = "binaryen.wasm-reduce";
    public const string BinaryenWasmShell = "binaryen.wasm-shell";
    public const string BinaryenWasm2Js = "binaryen.wasm2js";
    public const string JcoPackage = "jco.package";
    public const string Preview2ShimPackage = "preview2-shim.package";
    public const string JcoEntryPoint = "jco.entrypoint";
    public const string JcoPackageLock = "jco.package-lock";
    public const string JcoClosureIntegrity = "jco.closure-integrity";
    public const string JcoNotices = "jco.notices";
    public const string JcoClosurePolicy = "jco.closure-policy";
    public const string HostingBundleCommand = "hosting-bundle.command";
    public const string RolldownPackage = "rolldown.package";
    public const string RolldownEntryPoint = "rolldown.entrypoint";
    public const string HostingBundlePackageLock = "hosting-bundle.package-lock";
    public const string HostingBundleClosureIntegrity = "hosting-bundle.closure-integrity";
    public const string HostingBundleNotices = "hosting-bundle.notices";
    public const string HostingBundleClosurePolicy = "hosting-bundle.closure-policy";
    public const string WasmToolsCommand = "wasm-tools.command";
    public const string WasmToolsModule = "wasm-tools.module";
    public const string WasmToolsApacheLicense = "wasm-tools.license-apache";
    public const string WasmToolsApacheLlvmLicense = "wasm-tools.license-apache-llvm";
    public const string WasmToolsMitLicense = "wasm-tools.license-mit";
    public const string WasmToolsReadme = "wasm-tools.readme";
    public const string WitPackageManifest = "wit.package-manifest";
    public const string WitCommandPackage = "wit.command-package";
    public const string WitAsyncCommandPackage = "wit.async-command-package";
    public const string WitCompilerPackage = "wit.compiler-package";

    public static ImmutableArray<string> RawInspectionClosure { get; } =
    [
        RawInspectionCommand,
        RawInspectionCommandCore,
        RawInspectionError,
        RawImportInspector,
        CoreImportReader,
        BinaryenImportReader,
        BinaryenLicense,
        BinaryenReadme,
        BinaryenPackage,
        BinaryenModule,
        BinaryenTypes,
        BinaryenBinPackage,
        BinaryenWasmAs,
        BinaryenWasmCtorEval,
        BinaryenWasmDis,
        BinaryenWasmMerge,
        BinaryenWasmMetadce,
        BinaryenWasmOpt,
        BinaryenWasmReduce,
        BinaryenWasmShell,
        BinaryenWasm2Js,
    ];

    public static ImmutableArray<string> JcoClosure { get; } =
    [
        JcoPackage,
        Preview2ShimPackage,
        JcoEntryPoint,
        JcoPackageLock,
        JcoClosureIntegrity,
        JcoNotices,
        JcoClosurePolicy,
    ];

    public static ImmutableArray<string> HostingBundleClosure { get; } =
    [
        HostingBundleCommand,
        RolldownPackage,
        RolldownEntryPoint,
        HostingBundlePackageLock,
        HostingBundleClosureIntegrity,
        HostingBundleNotices,
        HostingBundleClosurePolicy,
    ];

    public static ImmutableArray<string> WasmToolsDistribution { get; } =
    [
        WasmToolsModule,
        WasmToolsApacheLicense,
        WasmToolsApacheLlvmLicense,
        WasmToolsMitLicense,
        WasmToolsReadme,
    ];

    public static ImmutableArray<string> WasmToolsClosure { get; } =
    [
        WasmToolsCommand,
        .. WasmToolsDistribution,
    ];

    public static ImmutableArray<string> WitPackageClosure { get; } =
    [
        WitPackageManifest,
        WitCommandPackage,
        WitAsyncCommandPackage,
        WitCompilerPackage,
    ];
}
