using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed record RawWitImportCatalog(
    WitDocument Document,
    WitWorld World,
    WasmTarget Target,
    ImmutableDictionary<RawCanonicalImportIdentity, RawWitImportDeclaration> Imports);
