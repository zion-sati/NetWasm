using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ITypeForwarderReader
{
    ImmutableDictionary<(AssemblyIdentity Assembly, string FullName), AssemblyIdentity> Read(
        IEnumerable<string> paths, ImmutableDictionary<string, string>? referenceAssemblyAliases = null,
        IEnumerable<string>? metadataReferencePaths = null);
}

public sealed class TypeForwarderReader(IManagedAssemblyImageReader images) : ITypeForwarderReader
{
    private readonly IManagedAssemblyImageReader _images = images ??
        throw new ArgumentNullException(nameof(images));

    public ImmutableDictionary<(AssemblyIdentity Assembly, string FullName), AssemblyIdentity> Read(
        IEnumerable<string> paths, ImmutableDictionary<string, string>? referenceAssemblyAliases = null,
        IEnumerable<string>? metadataReferencePaths = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var aliases = new AssemblyIdentityAliases(referenceAssemblyAliases ?? ImmutableDictionary<string, string>.Empty);
        var direct = new Dictionary<(AssemblyIdentity Assembly, string FullName), HashSet<AssemblyIdentity>>();
        foreach (var path in paths)
            ReadAssembly(path, direct);

        var result = ImmutableDictionary.CreateBuilder<(AssemblyIdentity, string), AssemblyIdentity>();
        foreach (var key in direct.Keys)
            Resolve(key.Assembly, key.FullName, []);
        if (metadataReferencePaths is not null)
        {
            // Runtime facades name their original platform owner. An explicit
            // metadata view supplies the application's replacement binding.
            // Validate runtime forwarding first so the overlay cannot hide a
            // malformed original graph, then validate the resulting binding graph.
            var views = new Dictionary<(AssemblyIdentity Assembly, string FullName), HashSet<AssemblyIdentity>>();
            foreach (var path in metadataReferencePaths) ReadAssembly(path, views);
            foreach (var (key, destinations) in views) direct[key] = destinations;
            result.Clear();
            foreach (var key in direct.Keys) Resolve(key.Assembly, key.FullName, []);
        }
        return result.ToImmutable();

        AssemblyIdentity Resolve(AssemblyIdentity owner, string name, HashSet<AssemblyIdentity> seen)
        {
            if (result.TryGetValue((owner, name), out var cached)) return cached;
            if (!direct.TryGetValue((owner, name), out var destinations)) return aliases.Canonicalize(owner);
            if (!seen.Add(owner))
                throw Invalid($"cyclic type forwarding for '{owner.Name}:{name}'");
            var terminals = new HashSet<AssemblyIdentity>();
            foreach (var destination in destinations)
                terminals.Add(Resolve(destination, name, seen));
            seen.Remove(owner);
            if (terminals.Count != 1)
                throw Invalid($"conflicting type forwarding for '{owner.Name}:{name}'");
            var terminal = terminals.Single();
            result.Add((owner, name), terminal);
            return terminal;
        }
    }

    private void ReadAssembly(string path,
        Dictionary<(AssemblyIdentity Assembly, string FullName), HashSet<AssemblyIdentity>> direct)
    {
        try
        {
            using var stream = new MemoryStream(_images.Read(path), writable: false);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            var owner = new AssemblyIdentity(metadata.GetString(metadata.GetAssemblyDefinition().Name));
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var handle in metadata.ExportedTypes)
            {
                var definition = metadata.GetExportedType(handle);
                if ((definition.Attributes & (TypeAttributes)0x00200000) == 0 &&
                    definition.Implementation.Kind != HandleKind.ExportedType)
                    continue;
                var (name, destination) = ReadExport(metadata, handle, []);
                if (!names.Add(name))
                    throw Invalid($"duplicate type forwarding for '{owner.Name}:{name}'");
                if (!direct.TryGetValue((owner, name), out var destinations))
                    direct.Add((owner, name), destinations = []);
                destinations.Add(destination);
            }
        }
        catch (BadImageFormatException error)
        {
            throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.UnsupportedMetadata,
                $"invalid managed metadata in '{Path.GetFileName(path)}': {error.Message}"));
        }
    }

    private static (string Name, AssemblyIdentity Destination) ReadExport(
        MetadataReader reader, ExportedTypeHandle handle, HashSet<ExportedTypeHandle> seen)
    {
        if (!seen.Add(handle))
            throw Invalid("cyclic nested exported type metadata");
        var definition = reader.GetExportedType(handle);
        var name = reader.GetString(definition.Name);
        var ns = reader.GetString(definition.Namespace);
        var implementation = definition.Implementation;
        if (implementation.Kind == HandleKind.AssemblyReference)
        {
            var reference = reader.GetAssemblyReference((AssemblyReferenceHandle)implementation);
            return (string.IsNullOrEmpty(ns) ? name : ns + "." + name,
                new AssemblyIdentity(reader.GetString(reference.Name)));
        }
        if (implementation.Kind == HandleKind.ExportedType)
        {
            var parent = ReadExport(reader, (ExportedTypeHandle)implementation, seen);
            return (parent.Name + "+" + name, parent.Destination);
        }
        throw Invalid("exported type forwarding requires an assembly or enclosing exported type");
    }

    private static CompilerException Invalid(string message) => new(
        new CompilerDiagnostic(DiagnosticCode.AssemblyResolution, message));
}
