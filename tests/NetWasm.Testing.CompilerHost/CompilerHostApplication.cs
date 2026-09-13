using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Testing.CompilerHost;

internal sealed class CompilerHostApplication(
    INetWasmCompiler compiler,
    IMetadataCompilationLoader metadataLoader)
{
    private readonly INetWasmCompiler _compiler =
        compiler ?? throw new ArgumentNullException(nameof(compiler));
    private readonly IMetadataCompilationLoader _metadataLoader =
        metadataLoader ?? throw new ArgumentNullException(nameof(metadataLoader));

    public int Run(CompilationRequest request, string responsePath)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(responsePath);
        try
        {
            var result = _compiler.Compile(new CompilerOptions(
                request.EntryAssemblyPath,
                request.ReferencePaths,
                request.EntryTypeName,
                request.EntryMethodName,
                request.Exports,
            request.Target,
            request.DiagnosticTracePath,
            WitPath: request.WitPath,
                WitWorld: request.WitWorld,
                SourcePaths: request.SourcePaths,
                ReferenceAssemblyAliases: request.ReferenceAssemblyAliases,
                EmitStackTrace: request.EmitStackTrace));
            File.WriteAllBytes(request.ModulePath, result.ApplicationModule);
            if (request.EmitStackTrace)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(
                    request.StackTraceSymbolsPath);
                File.WriteAllBytes(
                    request.StackTraceSymbolsPath,
                    result.StackTraceSymbols?.Bytes ?? throw new InvalidOperationException(
                        "instrumented compilation produced no stack-trace sidecar"));
            }

            using var metadata = request.ReferenceAssemblyAliases.Count == 0
                ? _metadataLoader.Load(request.EntryAssemblyPath, request.ReferencePaths)
                : _metadataLoader.Load(
                    request.EntryAssemblyPath,
                    request.ReferencePaths,
                    request.ReferenceAssemblyAliases);
            var typeNames = result.Layouts.TypeDescriptors.ToImmutableDictionary(
                descriptor => descriptor.TypeId,
                descriptor => metadata.Snapshot.Types
                    .Single(type => type.Key == descriptor.Type)
                    .FullName);
            File.WriteAllText(responsePath, JsonSerializer.Serialize(new CompilationResponse(
                typeNames,
                Convert.ToHexString(SHA256.HashData(result.ApplicationModule))
                    .ToLowerInvariant(),
                result.StaticDataEnd)));
            return 0;
        }
        catch (CompilerException exception) when (request.CaptureDiagnostic)
        {
            File.WriteAllText(responsePath, JsonSerializer.Serialize(
                new CapturedDiagnosticResponse(
                    (int)exception.Diagnostic.Code,
                    exception.Diagnostic.Message,
                    exception.Diagnostic.Method,
                    exception.Diagnostic.IlOffset)));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
