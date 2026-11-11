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
    public const string Preview3ShimPackage = "preview3-shim.package";
    public const string Preview3ShimLicense = "preview3-shim.license";
    public const string Preview3ShimHttp = "preview3-shim.http";
    public const string Preview3ShimHttpClient = "preview3-shim.http-client";
    public const string Preview3ShimHttpError = "preview3-shim.http-error";
    public const string Preview3ShimHttpFields = "preview3-shim.http-fields";
    public const string Preview3ShimHttpRequest = "preview3-shim.http-request";
    public const string Preview3ShimHttpResponse = "preview3-shim.http-response";
    public const string Preview3ShimHttpServer = "preview3-shim.http-server";
    public const string Preview3ShimFuture = "preview3-shim.future";
    public const string Preview3ShimStream = "preview3-shim.stream";
    public const string Preview3ShimHttpWorker = "preview3-shim.http-worker";
    public const string Preview3ShimResourceWorker = "preview3-shim.resource-worker";
    public const string Preview3ShimSockets = "preview3-shim.sockets";
    public const string Preview3ShimSocketsAddress = "preview3-shim.sockets-address";
    public const string Preview3ShimSocketsError = "preview3-shim.sockets-error";
    public const string Preview3ShimSocketsIpNameLookup = "preview3-shim.sockets-ip-name-lookup";
    public const string Preview3ShimSocketsTcp = "preview3-shim.sockets-tcp";
    public const string Preview3ShimSocketsUdp = "preview3-shim.sockets-udp";
    public const string Preview3ShimTcpWorker = "preview3-shim.tcp-worker";
    public const string Preview3ShimUdpWorker = "preview3-shim.udp-worker";
    public const string JcoEntryPoint = "jco.entrypoint";
    public const string JcoPackageLock = "jco.package-lock";
    public const string JcoClosureIntegrity = "jco.closure-integrity";
    public const string JcoNotices = "jco.notices";
    public const string JcoClosurePolicy = "jco.closure-policy";
    public const string JcoBindgenPatch = "jco.bindgen-patch";
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
    public const string WitDiagnosticCommandPackage = "wit.diagnostic-command-package";
    public const string WitCompilerPackage = "wit.compiler-package";
    public const string TimeZoneClosureIntegrity = "timezones.closure-integrity";

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
        Preview3ShimPackage,
        JcoEntryPoint,
        JcoPackageLock,
        JcoClosureIntegrity,
        JcoNotices,
        JcoClosurePolicy,
        JcoBindgenPatch,
    ];

    // The Node.js wasi:http client a deployment that imports wasi:http carries: preview3-shim's
    // HTTP entry point and, as its supporting files, its package description (which makes its
    // .js files ES modules), license, and the modules and workers the entry point loads.
    public static ImmutableArray<string> NodeHttpProviderFiles { get; } =
    [
        Preview3ShimPackage,
        Preview3ShimLicense,
        Preview3ShimHttpClient,
        Preview3ShimHttpError,
        Preview3ShimHttpFields,
        Preview3ShimHttpRequest,
        Preview3ShimHttpResponse,
        Preview3ShimHttpServer,
        Preview3ShimFuture,
        Preview3ShimStream,
        Preview3ShimHttpWorker,
        Preview3ShimResourceWorker,
    ];

    // The Node.js wasi:sockets implementation a deployment that imports wasi:sockets carries: preview3-shim's
    // sockets entry point and, as its supporting files, the package description, license, the TCP, UDP and name
    // lookup modules, and the workers that own the host sockets.
    public static ImmutableArray<string> NodeSocketsProviderFiles { get; } =
    [
        Preview3ShimPackage,
        Preview3ShimLicense,
        Preview3ShimSocketsAddress,
        Preview3ShimSocketsError,
        Preview3ShimSocketsIpNameLookup,
        Preview3ShimSocketsTcp,
        Preview3ShimSocketsUdp,
        Preview3ShimFuture,
        Preview3ShimStream,
        Preview3ShimTcpWorker,
        Preview3ShimUdpWorker,
        Preview3ShimResourceWorker,
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
        WitDiagnosticCommandPackage,
        WitCompilerPackage,
    ];
}
