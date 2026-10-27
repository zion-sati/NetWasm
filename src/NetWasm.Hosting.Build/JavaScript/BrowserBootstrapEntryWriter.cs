using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using NetWasm.Hosting.Build;

namespace NetWasm.Hosting.Build.JavaScript;

public sealed record BrowserJavaScriptModule(string Module, string SourcePath);

public sealed record BrowserBootstrapEntryRequest(
    string HostingBrowserModulePath,
    string Preview2ShimRoot,
    string OutputPath,
    ImmutableArray<BrowserJavaScriptModule> Modules = default);

public interface IBrowserBootstrapEntryWriter
{
    void Write(BrowserBootstrapEntryRequest request);
}

public sealed class BrowserBootstrapEntryWriter : IBrowserBootstrapEntryWriter
{
    public void Write(BrowserBootstrapEntryRequest request)
    {
        Validate(request);
        var modules = request.Modules.IsDefault ? [] : request.Modules;
        var builder = new StringBuilder();
        builder.Append("import { createBrowserNetWasmBootstrap } from ")
            .Append(ModuleSpecifier(request.HostingBrowserModulePath)).AppendLine(";");
        builder.Append("import { createCommandRun } from ")
            .Append(ModuleSpecifier(Path.Combine(Path.GetDirectoryName(request.HostingBrowserModulePath)!, "command-run.mjs")))
            .AppendLine(";");
        BrowserHostSource.AppendImports(builder, request.Preview2ShimRoot);
        foreach (var (module, index) in modules.Select((module, index) => (module, index)))
        {
            builder.Append("import * as consumerModule").Append(index).Append(" from ")
                .Append(ModuleSpecifier(module.SourcePath)).AppendLine(";");
        }
        builder.AppendLine();
        builder.AppendLine("const manifestUrl = new URL(\"./deployment.json\", import.meta.url).href;");
        builder.AppendLine("const preview2 = Object.freeze({");
        BrowserHostSource.AppendPreview2Members(builder, "  ");
        builder.AppendLine("});");
        builder.AppendLine("const web = Object.freeze({");
        BrowserHostSource.AppendWebMembers(builder, "  ");
        builder.AppendLine("});");
        builder.AppendLine();
        var moduleBindings = string.Join(", ", modules.Select((module, index) =>
            $"[{JsonSerializer.Serialize(module.Module, HostingBuildJsonContext.Default.String)}]: consumerModule{index}"));
        builder.AppendLine("export const executeNetWasm = createBrowserNetWasmBootstrap({");
        builder.AppendLine("  manifestUrl,");
        builder.AppendLine("  preview2,");
        builder.AppendLine("  web,");
        builder.Append("}, Object.freeze({ ").Append(moduleBindings).AppendLine(" }));");
        // The generated <App>.mjs run() helper calls this with the build's request.
        builder.AppendLine("export const runNetWasm = createCommandRun(executeNetWasm);");
        var source = builder.ToString().TrimEnd('\r', '\n');
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        File.WriteAllText(
            request.OutputPath,
            source.ReplaceLineEndings("\n") + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string ModuleSpecifier(string path) => BrowserHostSource.ModuleSpecifier(path);

    private static void Validate(BrowserBootstrapEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var path in new[]
                 {
                     request.HostingBrowserModulePath,
                     request.Preview2ShimRoot,
                     request.OutputPath,
                 })
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!Path.IsPathFullyQualified(path))
            {
                throw new ArgumentException(
                    "Browser bootstrap paths must be absolute.",
                    nameof(request));
            }
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in request.Modules.IsDefault ? [] : request.Modules)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(module.Module);
            if (!names.Add(module.Module) || !Path.IsPathFullyQualified(module.SourcePath))
                throw new ArgumentException("Browser JavaScript module bindings must be unique and use absolute source paths.", nameof(request));
        }
    }
}
