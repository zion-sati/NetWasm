using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

/// <summary>
/// Immutable state needed to emit one arbitrary decimal enum parse operation.
/// </summary>
internal sealed record EnumNumericParseEmissionRequest(
    CliTypeIdentity UnderlyingType,
    int TextLocal,
    EnumNumericParseOutput Output,
    int IndexLocal,
    int DigitLocal,
    bool IsArray = false);
