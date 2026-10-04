using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.Metadata.ManagedExecutables;

if (args is ["compile", var repository, var input, var application, var layout,
        .. var compileOptions])
{
    var structuredDiagnostics = compileOptions switch
    {
        [] => false,
        ["structured"] => true,
        _ => throw new ArgumentException(
            "Compile accepts an optional 'structured' diagnostics mode."),
    };
    var repo = Path.GetFullPath(repository);
    var assembly = Path.GetFullPath(input);
    var entry = new ManagedExecutableEntryPointSelector().SelectEntryPoint(
        assembly, File.ReadAllBytes(assembly));
    var coreLib = Path.Combine(repo, "src", "NetWasm.CoreLib", "bin",
        "Release", "net10.0", "NetWasm.CoreLib.dll");
    var result = NetWasmCompiler.Compile(new CompilerOptions(
        assembly,
        [coreLib],
        entry.TypeName,
        entry.MethodName,
        [],
        WasmTarget.Wasm32,
        WitPath: Path.Combine(repo, "wit", "netwasm-platform-1.0.0"),
        WitWorld: "netwasm:platform@1.0.0/platform",
        EntryMethodToken: entry.MetadataToken,
        EntryPointKind: CompilerEntryPointKind.ManagedExecutable,
        StructuredDiagnostics: structuredDiagnostics));
    if (result.StackTraceSymbols is not null)
    {
        throw new InvalidOperationException(
            "The Release size canary must keep rich managed stack traces disabled.");
    }
    File.WriteAllBytes(Path.GetFullPath(application), result.ApplicationModule);
    File.WriteAllText(Path.GetFullPath(layout), JsonSerializer.Serialize(new
    {
        schemaVersion = 2,
        target = "wasm32",
        applicationStaticDataEnd = result.StaticDataEnd,
        runtimeFeatures = result.RuntimeFeatures.Order(StringComparer.Ordinal).ToArray(),
    }));
    return;
}

if (args is ["package", var appModule, var runtimeModule, var commandWit,
        var component, .. var packageOptions])
{
    var (returnShape, structuredDiagnostics) = packageOptions switch
    {
        [] => (ManagedExecutableReturnShape.Void, false),
        ["int"] => (ManagedExecutableReturnShape.ExitCode, false),
        ["structured"] => (ManagedExecutableReturnShape.Void, true),
        ["int", "structured"] => (ManagedExecutableReturnShape.ExitCode, true),
        _ => throw new ArgumentException(
            "Package accepts an optional 'int' result shape followed by optional 'structured' diagnostics."),
    };
    using var services = new ServiceCollection()
        .AddNetWasmCompiler()
        .BuildServiceProvider();
    services.GetRequiredService<IComponentPackager>().Package(
        new ComponentPackageRequest(
            Path.GetFullPath(appModule),
            Path.GetFullPath(commandWit),
            structuredDiagnostics
                ? "netwasm:component/diagnostic-command@1.0.0"
                : "command",
            Path.GetFullPath(component),
            ComponentTarget.Wasm32Wasi02,
            RuntimeModulePath: Path.GetFullPath(runtimeModule),
            ManagedExecutableEntryPoint: new ManagedExecutableEntryPointAbi(
                ManagedExecutableParameterShape.None,
                returnShape),
            Optimization: FinalWasmOptimization.Oz,
            StructuredDiagnostics: structuredDiagnostics));
    return;
}

throw new ArgumentException("Expected compile or package arguments.");
