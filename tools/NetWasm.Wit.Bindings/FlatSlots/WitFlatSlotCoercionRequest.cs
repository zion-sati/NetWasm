using NetWasm.Compiler.Core;

namespace NetWasm.Wit.Bindings.FlatSlots;

public sealed record WitFlatSlotCoercionRequest(
    string Expression,
    CliValueKind Source,
    CliValueKind Destination);
