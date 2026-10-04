using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.StackTraces;

internal sealed class StackTraceSourceLocationReader(
    ISourceDocumentIdentityFormatter documents) : IStackTraceSourceLocationReader
{
    private static readonly Guid SourceLinkKind =
        new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

    private readonly ISourceDocumentIdentityFormatter _documents = documents ??
        throw new ArgumentNullException(nameof(documents));

    public ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>> Read(
        MetadataCompilationSnapshot metadata,
        CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.EmitStackTrace)
        {
            return ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>>.Empty;
        }

        var result = ImmutableDictionary.CreateBuilder<
            EntityKey,
            ImmutableArray<WasmSourceLocation>>();
        foreach (var assembly in metadata.Assemblies)
        {
            ReadAssembly(assembly, options, result);
        }
        return result.ToImmutable();
    }

    private void ReadAssembly(
        ManagedAssembly assembly,
        CompilerOptions options,
        ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>>.Builder result)
    {
        MetadataReaderProvider? provider = null;
        try
        {
            if (!assembly.PortableExecutableReader.TryOpenAssociatedPortablePdb(
                    assembly.Path,
                    static path => File.Exists(path) ? File.OpenRead(path) : null,
                    out provider,
                    out _))
            {
                return;
            }

            var reader = provider!.GetMetadataReader();
            var sourceLink = ReadSourceLink(reader);
            // Only physical IL offsets belong to the PDB. Synthesized accessor
            // wrappers have managed bodies but must retain method-only frames.
            var debugInformationRows = reader.GetTableRowCount(
                TableIndex.MethodDebugInformation);
            foreach (var item in assembly.Methods.Values
                         .Where(method => method.HasBody)
                         .Select(method => (
                             Method: method,
                             Row: MetadataTokens.GetRowNumber(
                                 (MethodDefinitionHandle)MetadataTokens.Handle(
                                     method.Key.MetadataToken))))
                         .Where(item => HasMethodDebugInformation(
                             item.Row,
                             debugInformationRows)))
            {
                var method = item.Method;
                var information = reader.GetMethodDebugInformation(
                    MetadataTokens.MethodDebugInformationHandle(item.Row));
                var locations = ImmutableArray.CreateBuilder<WasmSourceLocation>();
                foreach (var point in information.GetSequencePoints())
                {
                    if (point.IsHidden)
                    {
                        locations.Add(new(point.Offset, null, 0));
                        continue;
                    }
                    var documentHandle = SelectDocument(
                        point.Document,
                        information.Document);
                    locations.Add(CreateLocation(
                        point.Offset,
                        point.StartLine,
                        ReadDocument(reader, documentHandle),
                        sourceLink,
                        options));
                }
                if (locations.Count != 0)
                {
                    result[method.Key] = locations
                        .OrderBy(location => location.Offset)
                        .ToImmutableArray();
                }
            }
        }
        catch (BadImageFormatException)
        {
            // Missing, stale, or malformed symbols retain method-only frames.
        }
        catch (IOException)
        {
            // Unavailable symbols retain method-only frames.
        }
        catch (UnauthorizedAccessException)
        {
            // Inaccessible optional symbols retain method-only frames.
        }
        catch (JsonException)
        {
            // Invalid Source Link metadata retains method-only frames.
        }
        catch (ArgumentException)
        {
            // Invalid optional document paths retain method-only frames.
        }
        finally
        {
            provider?.Dispose();
        }
    }

    private static ImmutableArray<SourceLinkDocument> ReadSourceLink(
        MetadataReader reader)
    {
        foreach (var handle in reader.GetCustomDebugInformation(
                     MetadataTokens.EntityHandle(0x00000001)))
        {
            var information = reader.GetCustomDebugInformation(handle);
            if (reader.GetGuid(information.Kind) != SourceLinkKind)
            {
                continue;
            }
            using var document = JsonDocument.Parse(
                reader.GetBlobBytes(information.Value));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("documents", out var mappings) ||
                mappings.ValueKind != JsonValueKind.Object)
            {
                return [];
            }
            var result = ImmutableArray.CreateBuilder<SourceLinkDocument>();
            foreach (var mapping in mappings.EnumerateObject())
            {
                if (mapping.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                var url = mapping.Value.GetString()!;
                if (url.Length == 0) continue;
                result.Add(new(
                    mapping.Name.Replace('\\', '/'),
                    url));
            }
            return result.OrderByDescending(mapping => mapping.Pattern.Length)
                .ToImmutableArray();
        }
        return [];
    }

    private static string? MapSourceLink(
        string document,
        ImmutableArray<SourceLinkDocument> mappings)
    {
        var normalized = document.Replace('\\', '/');
        foreach (var mapping in mappings)
        {
            var wildcard = mapping.Pattern.IndexOf('*');
            if (wildcard < 0)
            {
                if (string.Equals(normalized, mapping.Pattern, StringComparison.Ordinal))
                {
                    return mapping.Url;
                }
                continue;
            }
            if (mapping.Pattern.IndexOf('*', wildcard + 1) >= 0)
            {
                continue;
            }
            var prefix = mapping.Pattern[..wildcard];
            var suffix = mapping.Pattern[(wildcard + 1)..];
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal) ||
                !normalized.EndsWith(suffix, StringComparison.Ordinal) ||
                normalized.Length < prefix.Length + suffix.Length)
            {
                continue;
            }
            var replacement = normalized.Substring(
                prefix.Length,
                normalized.Length - prefix.Length - suffix.Length);
            return mapping.Url.Replace("*", replacement, StringComparison.Ordinal);
        }
        return null;
    }

    internal static bool HasMethodDebugInformation(int row, int rowCount) =>
        row <= rowCount;

    internal static DocumentHandle SelectDocument(
        DocumentHandle sequencePointDocument,
        DocumentHandle methodDocument) => sequencePointDocument.IsNil
            ? methodDocument
            : sequencePointDocument;

    internal static string? ReadDocument(
        MetadataReader reader,
        DocumentHandle document) => document.IsNil
            ? null
            : reader.GetString(reader.GetDocument(document).Name);

    internal WasmSourceLocation CreateLocation(
        int offset,
        int startLine,
        string? document,
        ImmutableArray<SourceLinkDocument> sourceLink,
        CompilerOptions options)
    {
        if (string.IsNullOrWhiteSpace(document))
        {
            return new(offset, null, 0);
        }

        document = MapSourceLink(document, sourceLink) ?? document;
        return new(offset, _documents.Format(document, options), startLine);
    }

    internal readonly record struct SourceLinkDocument(string Pattern, string Url);
}
