using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed record MetadataCompilationSnapshot(
    ImmutableArray<ManagedAssembly> Assemblies,
    AssemblyIdentity EntryAssemblyIdentity,
    ImmutableArray<TypeDefinitionModel> Types,
    ImmutableArray<MethodDefinitionModel> Methods,
    ImmutableArray<MethodDefinitionModel> EntryAssemblyMethods,
    ImmutableDictionary<string, string>? ReferenceAssemblyAliases = null);
