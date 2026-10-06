using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NetWasm.Runtime.Pack.Planning;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeNativeModuleMaterializer(
    IRuntimeNativeLibraryResolver libraries,
    IRuntimeNativeProviderSelector providers,
    IRuntimeMemoryPlanBuilder memoryPlans,
    IRuntimeNativeArchiveValidationArgumentBuilder archiveArguments,
    IRuntimeLinkArgumentBuilder linkArguments,
    IRuntimeOptimizationArgumentBuilder optimizationArguments,
    ILinkedRuntimeModuleReader modules,
    ILinkedMemoryLayoutCalculator linkedLayouts,
    ILinkedMemoryLayoutValidator layoutValidator,
    ILinkerSymbolTraceReader traces,
    IRuntimeNativeBindingValidator bindingValidator,
    IRuntimeMaterializationCacheKeyBuilder cacheKeys,
    IRuntimeMaterializationCacheReader cacheReader,
    IRuntimeMaterializationCacheWriter cacheWriter,
    IRuntimeArtifactPublisher publisher,
    IRuntimeArtifactReader artifacts,
    IRuntimeNativeArchiveSnapshotter snapshots,
    ICommandInvoker commands,
    IRuntimeNativeLinkWorkspaceFactory workspaces,
    IRuntimeLinkExportPlanBuilder exports,
    IRuntimeLinkedImportValidator imports,
    IRuntimeNativeModuleValidator validation,
    IRuntimeNativeValidationProfileValidator profiles) : IRuntimeNativeModuleMaterializer
{
    public RuntimeMaterialization Materialize(RuntimeNativeMaterializationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Consumer);
        ArgumentNullException.ThrowIfNull(request.Manifest);
        ArgumentNullException.ThrowIfNull(request.Target);
        ArgumentNullException.ThrowIfNull(request.SourceLayout);
        var callbackSupport = request.SourceLayout.NativeCallbackSupport;
        if (request.SourceLayout.NativeImports.IsDefault ||
            request.SourceLayout.NativeImports.IsEmpty && callbackSupport is null ||
            request.SystemLibraryPaths.IsDefault ||
            request.SourceLayout.Target != request.Consumer.Target || request.Target.Target != request.Consumer.Target)
            throw new InvalidOperationException(
                "Native materialization requires reached native imports or callback support and runtime library paths.");
        var profile = request.Target.NativeValidation ??
            throw new InvalidOperationException("Static native calls require a runtime pack with an explicit native validation profile.");
        profiles.Validate(profile, request.Consumer.Target);
        var started = Stopwatch.GetTimestamp();
        var consumer = request.Consumer;
        var callbackObject = ReadCallbackObject(
            callbackSupport,
            consumer.NativeCallbackObjectPath,
            artifacts);
        var plan = memoryPlans.Build(new(request.Target, request.Manifest.WasmPageSize,
            request.SourceLayout.ApplicationStaticDataEnd, consumer.InitialHeapSizeBytes, consumer.MaximumMemorySizeBytes));
        var bindings = ImmutableArray<RuntimeNativeBinding>.Empty;
        if (!request.SourceLayout.NativeImports.IsEmpty)
        {
            var resolved = libraries.Resolve(new(
                consumer.Target,
                request.SourceLayout.NativeImports,
                consumer.NativeLibraries));
            bindings = providers.Select(new(
                consumer.Target,
                request.SourceLayout.NativeImports,
                resolved));
        }
        var exportRequest = new RuntimeLinkExportPlanRequest(
            request.Manifest.Exports,
            bindings)
        {
            RuntimeFeatures = request.SourceLayout.RuntimeFeatures,
            NativeCallbackSupport = callbackSupport,
        };
        var internalExports = exports.Build(exportRequest).InternalExports;
        var identityCallbackAllowPath = callbackSupport is null
            ? null
            : Path.GetFullPath(consumer.NativeCallbackObjectPath!) + ".allow-undefined";
        var identityLink = new RuntimeLinkRequest(request.Manifest, request.Target,
            new(plan.RuntimeGlobalBase, null, plan.MaximumMemorySizeBytes), consumer.AssetRoot,
            request.SystemLibraryPaths, consumer.OutputPath)
        {
            RuntimeFeatures = request.SourceLayout.RuntimeFeatures,
            NativeBindings = bindings,
            NativeCallbackObjectPath = callbackSupport is null
                ? null
                : consumer.NativeCallbackObjectPath,
            NativeCallbackAllowedUndefinedPath = identityCallbackAllowPath,
            NativeCallbackSupport = callbackSupport,
        };
        var identityArguments = linkArguments.Build(identityLink);
        var identityOptimization = optimizationArguments.Build(new(
            request.Target,
            consumer.OutputPath,
            consumer.Optimization)
        {
            RuntimeGlobalBase = plan.RuntimeGlobalBase,
        });
        var keyRequest = new RuntimeNativeMaterializationCacheKeyRequest(consumer.BuildIdentity, request.Manifest,
            request.Target, plan, bindings, consumer.Optimization, identityArguments, identityOptimization,
            consumer.AssetRoot, consumer.OutputPath)
        {
            NativeCallbackSupport = callbackSupport,
            NativeCallbackObjectPath = identityLink.NativeCallbackObjectPath,
            NativeCallbackAllowedUndefinedPath =
                identityLink.NativeCallbackAllowedUndefinedPath,
        };
        var key = cacheKeys.Build(keyRequest);
        var slot = new RuntimeMaterializationCacheSlot(consumer.Target, consumer.Optimization);
        var lookupStarted = Stopwatch.GetTimestamp();
        var cached = cacheReader.Read(consumer.CacheDirectory, slot, key);
        var lookupMilliseconds = Stopwatch.GetElapsedTime(lookupStarted).TotalMilliseconds;
        if (cached.Outcome == RuntimeMaterializationCacheOutcome.Hit)
        {
            try
            {
                if (cached.NativeEvidence is null || cached.NativeEvidence.Layout is null ||
                    cached.NativeEvidence.Bindings.IsDefault || cached.NativeEvidence.Bindings.Any(binding => binding is null) ||
                    cached.Bytes is null || cached.Sha256 is null)
                    throw new InvalidOperationException("The native cache entry lacks linked evidence.");
                var module = modules.Read(cached.Bytes);
                imports.Validate(profile, module, callbackSupport);
                layoutValidator.Validate(plan, module.MemoryLayout, cached.NativeEvidence.Layout);
                var expected = linkedLayouts.Calculate(plan, module.MemoryLayout);
                layoutValidator.Validate(plan, module.MemoryLayout, expected);
                bindingValidator.Validate(new RuntimeNativeCachedBindingValidationRequest(bindings, cached.NativeEvidence.Bindings, module));
            }
            catch (InvalidOperationException)
            {
                cached = new(RuntimeMaterializationCacheOutcome.Corrupt, null, null);
            }
            if (cached.Outcome == RuntimeMaterializationCacheOutcome.Hit)
            {
                using var warmWorkspace = workspaces.Create(consumer.LogDirectory);
                var warmOutput = Path.Combine(warmWorkspace.DirectoryPath, "cached-runtime.wasm");
                publisher.PublishIfDifferent(warmOutput, cached.Bytes!, cached.Sha256!);
                validation.Validate(new(profile, consumer.Target, warmOutput, consumer.WasmToolsNodePath,
                    consumer.WasmToolsCommandPath, consumer.WasmToolsModulePath,
                    Path.Combine(warmWorkspace.LogDirectoryPath, "native-cache-validate.log")));
                publisher.PublishIfDifferent(consumer.OutputPath, cached.Bytes!, cached.Sha256!);
                return Result(request, cached.NativeEvidence!.Layout, cached.Sha256!, key, cached.Outcome,
                    false, cached.Bytes!.LongLength, lookupMilliseconds, started, internalExports);
            }
        }

        // Link into an owned workspace. Only the fully validated bytes are published.
        using var workspace = workspaces.Create(consumer.LogDirectory);
        var output = Path.Combine(workspace.DirectoryPath, "runtime.wasm");
        string? ownedCallbackPath = null;
        string? ownedCallbackAllowPath = null;
        if (callbackSupport is not null)
        {
            ownedCallbackPath = Path.Combine(
                workspace.DirectoryPath,
                callbackSupport.FileName);
            publisher.PublishIfDifferent(
                ownedCallbackPath,
                callbackObject!,
                callbackSupport.Sha256);
            ownedCallbackAllowPath = Path.Combine(
                workspace.DirectoryPath,
                "combined.allow-undefined");
            var runtimeAllowedPath = Path.GetFullPath(Path.Combine(
                consumer.AssetRoot,
                request.Target.AllowedUndefinedSymbols.Path));
            var allowed = CombineAllowedUndefinedSymbols(
                artifacts.Read(runtimeAllowedPath),
                callbackSupport.Callbacks.Select(
                    callback => callback.RuntimeImportSymbol));
            publisher.PublishIfDifferent(
                ownedCallbackAllowPath,
                allowed,
                Convert.ToHexString(SHA256.HashData(allowed)).ToLowerInvariant());
        }
        var selectedArchives = bindings.Select(binding => binding.Provider)
            .DistinctBy(provider => provider.Path, StringComparer.Ordinal).ToImmutableArray();
        var ownedArchives = selectedArchives.IsEmpty
            ? []
            : snapshots.Snapshot(new(selectedArchives, workspace.DirectoryPath));
        var ownedByPath = ownedArchives.ToDictionary(archive => archive.Provider.Path, StringComparer.Ordinal);
        var ownedBindings = bindings.Select(binding => binding with
        {
            Provider = binding.Provider with { Path = ownedByPath[binding.Provider.Path].SnapshotPath },
        }).ToImmutableArray();
        for (var index = 0; index < ownedArchives.Length; index++)
        {
            var provider = ownedArchives[index];
            commands.Invoke(new(consumer.WasmLdPath,
                archiveArguments.Build(new(consumer.Target, provider.SnapshotPath, Path.Combine(workspace.DirectoryPath, $"archive-{index}.o"))),
                Path.Combine(workspace.LogDirectoryPath, $"native-archive-validation-{index}.log")));
        }
        var link = identityLink with
        {
            OutputPath = output,
            NativeBindings = ownedBindings,
            NativeCallbackObjectPath = ownedCallbackPath,
            NativeCallbackAllowedUndefinedPath = ownedCallbackAllowPath,
        };
        var generated = ImmutableArray.Create(output + ".lto.o");
        var probeLog = Path.Combine(workspace.LogDirectoryPath, "native-probe-link.log");
        commands.Invoke(new(consumer.WasmLdPath, linkArguments.Build(link), probeLog));
        var probe = modules.Read(artifacts.Read(output));
        imports.Validate(profile, probe, callbackSupport);
        bindingValidator.Validate(new RuntimeNativeBindingValidationRequest(ownedBindings, generated,
            traces.Read(Encoding.UTF8.GetString(artifacts.Read(probeLog))), probe));
        var finalLayout = linkedLayouts.Calculate(plan, probe.MemoryLayout);
        link = link with { Layout = link.Layout with { InitialMemorySizeBytes = finalLayout.InitialMemorySizeBytes } };
        var finalLog = Path.Combine(workspace.LogDirectoryPath, "native-final-link.log");
        commands.Invoke(new(consumer.WasmLdPath, linkArguments.Build(link), finalLog));
        var finalTrace = traces.Read(Encoding.UTF8.GetString(artifacts.Read(finalLog)));
        var linked = modules.Read(artifacts.Read(output));
        imports.Validate(profile, linked, callbackSupport);
        layoutValidator.Validate(plan, linked.MemoryLayout, finalLayout);
        bindingValidator.Validate(new RuntimeNativeBindingValidationRequest(ownedBindings, generated, finalTrace, linked));
        if (consumer.Optimization != RuntimeWasmOptimization.None)
            commands.Invoke(new(consumer.WasmOptPath,
                optimizationArguments.Build(new(request.Target, output, consumer.Optimization)
                {
                    RuntimeGlobalBase = plan.RuntimeGlobalBase,
                }),
                Path.Combine(workspace.LogDirectoryPath, "native-optimize.log")));
        validation.Validate(new(profile, consumer.Target, output, consumer.WasmToolsNodePath,
            consumer.WasmToolsCommandPath, consumer.WasmToolsModulePath,
            Path.Combine(workspace.LogDirectoryPath, "native-validate.log")));
        var bytes = artifacts.Read(output);
        var optimized = modules.Read(bytes);
        imports.Validate(profile, optimized, callbackSupport);
        layoutValidator.Validate(plan, optimized.MemoryLayout, finalLayout);
        var validated = bindingValidator.Validate(new RuntimeNativeBindingValidationRequest(ownedBindings, generated, finalTrace, optimized));
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        // Provenance was observed against verified snapshots. Persist the stable
        // provider identity, not an invocation-local path that is about to vanish.
        var evidence = new RuntimeNativeCacheEvidence(finalLayout, validated.Select((binding, index) =>
            binding with { ProviderPath = bindings[index].Provider.Path }).ToImmutableArray());
        try
        {
            cacheWriter.Write(consumer.CacheDirectory, slot, key, bytes, digest, evidence);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Optional cache storage cannot invalidate the fully validated artifact.
        }
        publisher.PublishIfDifferent(consumer.OutputPath, bytes, digest);
        return Result(request, finalLayout, digest, key, cached.Outcome, true, bytes.LongLength, lookupMilliseconds, started, internalExports);
    }

    private static byte[]? ReadCallbackObject(
        RuntimeNativeCallbackSupport? support,
        string? path,
        IRuntimeArtifactReader artifacts)
    {
        if (support is null)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                throw new InvalidOperationException(
                    "The compiler callback support object has no runtime layout authority.");
            }
            return null;
        }
        if (string.IsNullOrWhiteSpace(path) ||
            !string.Equals(Path.GetFileName(path), support.FileName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The compiler callback support object identity is invalid.");
        }
        var bytes = artifacts.Read(path);
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(digest, support.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The compiler callback support object digest is invalid.");
        }
        return bytes;
    }

    private static byte[] CombineAllowedUndefinedSymbols(
        byte[] runtimeSymbols,
        IEnumerable<string> callbackSymbols)
    {
        ArgumentNullException.ThrowIfNull(runtimeSymbols);
        ArgumentNullException.ThrowIfNull(callbackSymbols);
        using var combined = new MemoryStream();
        combined.Write(runtimeSymbols);
        if (runtimeSymbols.Length > 0 && runtimeSymbols[^1] != (byte)'\n')
            combined.WriteByte((byte)'\n');
        var callbacks = Encoding.UTF8.GetBytes(
            string.Join('\n', callbackSymbols) + "\n");
        combined.Write(callbacks);
        return combined.ToArray();
    }

    private static RuntimeMaterialization Result(RuntimeNativeMaterializationRequest request, RuntimeLinkedMemoryLayout layout,
        string digest, RuntimeMaterializationCacheKey key, RuntimeMaterializationCacheOutcome outcome,
        bool recomputed, long bytes, double lookupMilliseconds, long started, ImmutableArray<RuntimeLinkExport> internalExports) =>
        new(request.Target.Target, Path.GetFullPath(request.Consumer.OutputPath), digest, request.Manifest.RuntimeAbi,
            request.Manifest.Provenance.BuildSeam, request.Manifest.Provenance.ToolchainFingerprint,
            layout.RuntimeGlobalBase, layout.HeapBase, layout.InitialMemorySizeBytes, layout.MaximumMemorySizeBytes,
            new("runtime-materialization", key.Prefix, outcome, recomputed, bytes, lookupMilliseconds,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds))
        {
            GarbageCollector = request.Target.GarbageCollector,
            InternalRuntimeExports = internalExports,
            InternalApplicationExports = request.SourceLayout.NativeCallbackSupport is null
                ? []
                : [.. request.SourceLayout.NativeCallbackSupport
                    .TemporaryApplicationExports.Select(name =>
                        new RuntimeLinkExport(name, 0))],
        };
}
