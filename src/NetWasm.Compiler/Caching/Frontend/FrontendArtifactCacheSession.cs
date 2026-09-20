using NetWasm.Compiler.Analysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Caching.Frontend;

internal interface IFrontendArtifactCacheRequestFactory
{
    IFrontendArtifactCacheRequest Begin(CompilerOptions options);
}

internal interface IFrontendArtifactCacheRequest : IDisposable
{
    void Commit();
}

internal interface IFrontendArtifactCacheRequestResolver
{
    FrontendArtifactCacheRequest? Resolve();
}

internal interface IFrontendArtifactCacheMetricsReader
{
    FrontendArtifactCacheMetrics Read();
}

internal interface IFrontendArtifactCacheIdentityBuilder
{
    FrontendArtifactCacheContext? Build(CompilerOptions options);
}

internal interface IFrontendArtifactCompilerIdentity
{
    string Read();
}

internal sealed class FrontendArtifactCompilerIdentity : IFrontendArtifactCompilerIdentity
{
    private static readonly Type[] ComponentAnchors =
    [
        typeof(FrontendArtifactCompilerIdentity),
        typeof(CompilerException),
        typeof(IManagedAssemblyImageReader),
        typeof(IControlFlowGraphAnalyzerFactory),
        typeof(IRootMapAnalyzer),
        typeof(IWasmModuleBuilder),
        typeof(IComponentPackageExecution),
    ];

    public string Read() => FrontendArtifactCacheContext.Hash(string.Join('\n',
        ComponentAnchors.Select(static type => type.Assembly)
            .Distinct()
            .Select(static assembly => (
                Name: assembly.GetName().Name ??
                    throw new InvalidOperationException(
                        "A compiler component assembly has no name."),
                Mvid: assembly.ManifestModule.ModuleVersionId))
            .OrderBy(static component => component.Name, StringComparer.Ordinal)
            .Select(static component => component.Name + "|" +
                component.Mvid.ToString("N"))));
}

internal sealed class FrontendArtifactCacheState
{
    private readonly AsyncLocal<FrontendArtifactCacheRequest?> _active = new();

    internal FrontendArtifactCacheRequest? Active
    {
        get => _active.Value;
        set => _active.Value = value;
    }
}

internal sealed class FrontendArtifactCacheRequestFactory(
    FrontendArtifactCacheState state,
    FrontendArtifactTransportStore transport,
    FrontendArtifactMemoryStore memory,
    IFrontendArtifactCacheIdentityBuilder identities,
    IFrontendArtifactPayloadPublisher payloadPublisher,
    IFrontendArtifactObjectPublisher objectPublisher) : IFrontendArtifactCacheRequestFactory
{
    internal FrontendArtifactCacheRequestFactory(
        FrontendArtifactCacheState state,
        IFrontendArtifactCacheIdentityBuilder identities,
        IFrontendArtifactPayloadPublisher payloadPublisher,
        IFrontendArtifactObjectPublisher objectPublisher) :
        this(state, new(), new(), identities, payloadPublisher, objectPublisher)
    {
    }

    public IFrontendArtifactCacheRequest Begin(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (state.Active is not null)
            throw new InvalidOperationException("The frontend artifact cache already has an active compilation.");
        var active = transport.Active.Value;
        var context = options.EnableFrontendCache
            ? active?.Context ?? identities.Build(options) : null;
        var budget = active?.Budget ?? new FrontendArtifactWorkingSetBudget();
        if (active is null && context is not null)
            lock (memory.Gate)
            {
                if (memory.Namespace is not null && !string.Equals(memory.Namespace,
                        context.Namespace, StringComparison.Ordinal))
                {
                    memory.Payloads.Clear();
                    memory.LoadedNamespaces.Clear();
                    memory.Namespace = null;
                    memory.EntryCount = 0;
                    memory.TotalBytes = 0;
                }
                if (!budget.TryReserve(memory.EntryCount, memory.TotalBytes, 0))
                    throw new InvalidOperationException(
                        "The frontend artifact memory store exceeds its policy.");
            }
        state.Active = new(state, context, budget,
            payloadPublisher, objectPublisher);
        return state.Active;
    }
}

internal sealed class FrontendArtifactCacheRequestResolver(
    FrontendArtifactCacheState state) : IFrontendArtifactCacheRequestResolver
{
    public FrontendArtifactCacheRequest? Resolve() => state.Active;
}

internal sealed class FrontendArtifactCacheRequest(
    FrontendArtifactCacheState state,
    FrontendArtifactCacheContext? context,
    FrontendArtifactWorkingSetBudget budget,
    IFrontendArtifactPayloadPublisher payloadPublisher,
    IFrontendArtifactObjectPublisher objectPublisher) : IFrontendArtifactCacheRequest
{
    private bool _committed;
    private bool _disposed;
    internal FrontendArtifactCacheContext? Context { get; } = context;
    internal ConcurrentDictionary<string, ReachableMethodAnalysis> Analyses { get; } = new(StringComparer.Ordinal);
    internal ConcurrentDictionary<string, StructuredMethod> Restored { get; } = new(StringComparer.Ordinal);
    internal ConcurrentDictionary<string, byte> RestoredStructuralKeys { get; } =
        new(StringComparer.Ordinal);
    internal ConcurrentDictionary<string, ImmutableArray<byte>> Staged { get; } = new(StringComparer.Ordinal);
    internal ConcurrentDictionary<string, FrontendArtifact> StagedObjects { get; } = new(StringComparer.Ordinal);
    internal ConcurrentDictionary<string, long> StagedStructuralBytes { get; } =
        new(StringComparer.Ordinal);
    internal FrontendArtifactWorkingSetBudget Budget { get; } = budget;
    internal object StagingGate { get; } = new();
    internal long StagedBytes;
    internal long Lookups;
    internal long Hits;
    internal long Misses;
    internal long MemoryHits;
    internal long DiskHits;
    internal long StagedArtifacts;

    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _committed = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            if (ReferenceEquals(state.Active, this) && _committed && Context is not null)
            {
                payloadPublisher.Publish(new(Context, Staged));
                objectPublisher.Publish(new(Context, StagedObjects,
                    StagedStructuralBytes));
            }
        }
        finally
        {
            if (ReferenceEquals(state.Active, this)) state.Active = null;
            _disposed = true;
        }
    }
}

internal sealed class FrontendArtifactCacheMetricsReader(
    IFrontendArtifactCacheRequestResolver requests) : IFrontendArtifactCacheMetricsReader
{
    public FrontendArtifactCacheMetrics Read()
    {
        var request = requests.Resolve();
        if (request is null) return new(0, 0, 0, 0, 0, 0, 0);
        return new(request.Lookups, request.Hits, request.Misses, request.MemoryHits,
            request.DiskHits, request.StagedArtifacts, request.StagedBytes);
    }
}

internal sealed record FrontendArtifactCacheContext(
    string Namespace,
    AssemblyIdentity EntryAssembly,
    ImmutableDictionary<string, string> ContentHashes,
    string? Directory)
{
    internal bool IsEligible(MethodInstanceModel method) =>
        method.Definition.Key.Assembly != EntryAssembly &&
        !References(method.DeclaringType, EntryAssembly) &&
        !method.MethodArguments.Any(argument => References(argument, EntryAssembly)) &&
        ContentHashes.ContainsKey(method.Definition.Key.Assembly.Name);

    internal string MethodKey(MethodInstanceModel method) => Hash(string.Join('|', ContentHashes[method.Definition.Key.Assembly.Name], method.CanonicalName));

    private static bool References(CliTypeIdentity type, AssemblyIdentity entry) =>
        type.Assembly == entry || type.ElementType is not null && References(type.ElementType, entry) ||
        type.TypeArguments.Any(argument => References(argument, entry)) ||
        type.StackStorageType is not null && References(type.StackStorageType, entry);

    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

internal sealed class FrontendArtifactCacheIdentityBuilder(
    IEntryAssemblyBindingFingerprinter bindingFingerprints,
    IManagedAssemblyImageReader images,
    ICompilationInputHasher inputHasher,
    IFrontendArtifactCompilerIdentity compilerIdentity) :
    IFrontendArtifactCacheIdentityBuilder
{
    internal const string Schema = "frontend-artifact-cache-v4";
    private readonly IEntryAssemblyBindingFingerprinter _bindingFingerprints =
        bindingFingerprints ?? throw new ArgumentNullException(nameof(bindingFingerprints));
    private readonly IManagedAssemblyImageReader _images = images ??
        throw new ArgumentNullException(nameof(images));
    private readonly ICompilationInputHasher _inputHasher = inputHasher ??
        throw new ArgumentNullException(nameof(inputHasher));
    private readonly IFrontendArtifactCompilerIdentity _compilerIdentity =
        compilerIdentity ?? throw new ArgumentNullException(nameof(compilerIdentity));

    public FrontendArtifactCacheContext? Build(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            var entry = ReadAssembly(options.EntryAssemblyPath, true);
            var references = options.ReferencePaths.Select(path => ReadAssembly(path, false)).ToArray();
            var hashes = references.Append(entry).ToImmutableDictionary(static item => item.Identity.Name, static item => item.ContentHash, StringComparer.Ordinal);
            var aliases = options.ReferenceAssemblyAliases ?? ImmutableDictionary<string, string>.Empty;
            var optionsIdentity = string.Join('|', Schema, options.Target, options.EntryPointKind,
                options.EntryTypeName, options.EntryMethodName, options.EntryMethodToken,
                options.EmitExceptionTypeMap, options.EmitStackTrace, options.WitWorld,
                HashOptionalInput(options.WitPath),
                string.Join('\n', options.Exports.OrderBy(static value => value.Name, StringComparer.Ordinal).Select(static value => $"{value.Name}|{value.TypeName}|{value.MethodName}")),
                string.Join('\n', aliases.OrderBy(static value => value.Key, StringComparer.Ordinal).Select(static value => $"{value.Key}={value.Value}")),
                _compilerIdentity.Read());
            var universe = string.Join('\n', references.Select(static item => $"{item.Identity.Name}|{item.ContentHash}"));
            var cacheNamespace = FrontendArtifactCacheContext.Hash(optionsIdentity + "\n" + entry.BindingFingerprint + "\n" + universe);
            var directory = OperatingSystem.IsBrowser() || string.IsNullOrWhiteSpace(options.IntermediateOutputPath)
                ? null : Path.Combine(Path.GetFullPath(options.IntermediateOutputPath), "netwasm", Schema, cacheNamespace);
            return new(cacheNamespace, entry.Identity, hashes, directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            BadImageFormatException or InvalidDataException or CryptographicException or
            ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private AssemblyInput ReadAssembly(string path, bool bindingFingerprint)
    {
        var bytes = _images.Read(path);
        using var stream = new MemoryStream(bytes, writable: false);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var definition = metadata.GetAssemblyDefinition();
        return new(new(metadata.GetString(definition.Name)),
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            bindingFingerprint ? _bindingFingerprints.Fingerprint([.. bytes]) : "");
    }

    private string HashOptionalInput(string? path) => path is { Length: > 0 }
        ? _inputHasher.Hash(path) : "";

    private sealed record AssemblyInput(AssemblyIdentity Identity, string ContentHash, string BindingFingerprint);
}
