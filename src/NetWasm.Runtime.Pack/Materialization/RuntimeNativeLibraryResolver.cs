using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeNativeLibraryResolver(IRuntimeNativeArchiveReader archives) : IRuntimeNativeLibraryResolver
{
    private readonly IRuntimeNativeArchiveReader _archives = archives ?? throw new ArgumentNullException(nameof(archives));

    public ImmutableArray<RuntimeNativeLibrary> Resolve(RuntimeNativeLibraryResolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Target is not ("wasm32" or "wasm64") || request.Imports.IsDefault || request.Providers.IsDefault)
            throw new InvalidOperationException("The native library resolution request is invalid.");
        // Validate descriptor metadata before the first archive read. Conflict and ABI policy
        // belong to RuntimeNativeProviderSelector, after current byte identities are known.
        foreach (var descriptor in request.Providers)
        {
            if (descriptor is null || string.IsNullOrWhiteSpace(descriptor.LibraryName) ||
                descriptor.LibraryName.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
                descriptor.Target is not ("wasm32" or "wasm64") || string.IsNullOrWhiteSpace(descriptor.Path) ||
                !Path.IsPathFullyQualified(descriptor.Path) || descriptor.Path.IndexOfAny(['\0', '\r', '\n']) >= 0)
                throw new InvalidOperationException("The native library descriptor metadata is invalid.");
        }
        if (request.Imports.Any(import => import is null || string.IsNullOrWhiteSpace(import.LibraryName)))
            throw new InvalidOperationException("The native import library identity is invalid.");
        var needed = request.Imports.Select(import => import.LibraryName).ToHashSet(StringComparer.Ordinal);
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = ImmutableArray.CreateBuilder<RuntimeNativeLibrary>();
        foreach (var descriptor in request.Providers)
        {
            if (descriptor.Target != request.Target || !needed.Contains(descriptor.LibraryName))
                continue;
            var path = Path.GetFullPath(descriptor.Path);
            if (!digests.TryGetValue(path, out var digest))
            {
                digest = _archives.Read(path);
                digests.Add(path, digest);
            }
            result.Add(new(descriptor.LibraryName, descriptor.Target, path, digest));
        }
        return result.ToImmutable();
    }
}
