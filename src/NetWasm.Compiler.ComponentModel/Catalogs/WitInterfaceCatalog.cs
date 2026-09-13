using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Catalogs;

public sealed record WitInterfaceCatalog(
    string World,
    string NormalizedWitJson,
    ImmutableArray<WitInterfaceContract> Interfaces);

public sealed record WitInterfaceContract(
    string Module,
    ImmutableArray<WitInterfaceFunction> Functions);

public sealed record WitInterfaceFunction(
    string Interface,
    string Name,
    ImmutableArray<string> Parameters,
    ImmutableArray<string> Results);
