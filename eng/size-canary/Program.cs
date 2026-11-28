// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

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
    var entry = new ManagedExecutableEntryPointSelectorFactory().Create().SelectEntryPoint(
        assembly, File.ReadAllBytes(assembly));
    var coreLib = Path.Combine(repo, "src", "NetWasm.CoreLib", "bin",
        "Release", "net10.0", "NetWasm.CoreLib.dll");
    // The CLI, build tasks and browser compiler compose their compiler from the
    // same registration.
    using var compilerServices = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
    var result = compilerServices.GetRequiredService<INetWasmCompiler>().Compile(new CompilerOptions(
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
    if (result.MethodSymbols is not null)
    {
        throw new InvalidOperationException(
            "The Release size canary must keep rich managed stack traces disabled.");
    }
    File.WriteAllBytes(Path.GetFullPath(application), result.ApplicationModule);
    // The layout nwc writes (schema 3): the runtime features the program uses and its reached native imports.
    // Native callbacks need schema 4 and their object file, which this canary does not build.
    if (result.NativeCallbackSupport is not null)
    {
        throw new InvalidOperationException("the size canary writes no native callback support");
    }
    File.WriteAllText(Path.GetFullPath(layout), JsonSerializer.Serialize(new
    {
        schemaVersion = 3,
        target = "wasm32",
        applicationStaticDataEnd = result.StaticDataEnd,
        managedExecutableEntryPoint = (object?)null,
        runtimeFeatures = result.RuntimeFeatures.Order(StringComparer.Ordinal).ToArray(),
        importModules = Array.Empty<string>(),
        nativeImports = result.NativeImports.Select(static import => new
        {
            libraryName = import.LibraryName,
            entryPoint = import.EntryPoint,
            parameters = import.Parameters.Select(ValueTypeName).ToArray(),
            returnType = import.ReturnType is { } returnType ? ValueTypeName(returnType) : null,
        }).ToArray(),
    }));
    static string ValueTypeName(NetWasm.Compiler.Wasm.WasmValueType type) =>
        JsonNamingPolicy.CamelCase.ConvertName(type.ToString());
    return;
}

if (args is ["package", var appModule, var runtimeModule, var commandWit,
        var component, .. var packageOptions])
{
    // Options, in order: 'int' (an exit code), 'async' (a callback-lifted asynchronous Main),
    // 'structured' (structured diagnostics).
    string[] known = ["int", "async", "structured"];
    if (packageOptions.Length > known.Length ||
        packageOptions.Where((option, index) => Array.IndexOf(known, option) < 0 ||
            (index > 0 && Array.IndexOf(known, option) <= Array.IndexOf(known, packageOptions[index - 1]))).Any())
    {
        throw new ArgumentException(
            "Package accepts optional 'int', 'async' and 'structured' options, in that order.");
    }
    var returnShape = packageOptions.Contains("int")
        ? ManagedExecutableReturnShape.ExitCode
        : ManagedExecutableReturnShape.Void;
    var completionShape = packageOptions.Contains("async")
        ? ManagedExecutableCompletionShape.Asynchronous
        : ManagedExecutableCompletionShape.Synchronous;
    var structuredDiagnostics = packageOptions.Contains("structured");
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
                returnShape,
                completionShape),
            Optimization: FinalWasmOptimization.Oz,
            StructuredDiagnostics: structuredDiagnostics));
    return;
}

throw new ArgumentException("Expected compile or package arguments.");
