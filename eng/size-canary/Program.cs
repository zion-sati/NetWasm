using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.Metadata.ManagedExecutables;

if (args is ["compile", var repository, var input, var application, var layout])
{
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
        EntryPointKind: CompilerEntryPointKind.ManagedExecutable));
    File.WriteAllBytes(Path.GetFullPath(application), result.ApplicationModule);
    File.WriteAllText(Path.GetFullPath(layout), JsonSerializer.Serialize(new
    {
        schemaVersion = 2,
        target = "wasm32",
        applicationStaticDataEnd = result.StaticDataEnd,
    }));
    return;
}

if (args is ["package", var appModule, var runtimeModule, var commandWit,
        var component])
{
    using var services = new ServiceCollection()
        .AddNetWasmCompiler()
        .BuildServiceProvider();
    services.GetRequiredService<IComponentPackager>().Package(
        new ComponentPackageRequest(
            Path.GetFullPath(appModule),
            Path.GetFullPath(commandWit),
            "command",
            Path.GetFullPath(component),
            ComponentTarget.Wasm32Wasi02,
            RuntimeModulePath: Path.GetFullPath(runtimeModule),
            ManagedExecutableEntryPoint: new ManagedExecutableEntryPointAbi(
                ManagedExecutableParameterShape.None,
                ManagedExecutableReturnShape.Void),
            Optimization: FinalWasmOptimization.Size));
    return;
}

throw new ArgumentException("Expected compile or package with four paths.");
