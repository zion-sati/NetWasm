using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed record EnumStorage(
    TypeDescriptorLayout Descriptor,
    CliTypeIdentity EnumType,
    CliTypeIdentity UnderlyingType,
    ValueLayout Layout,
    int PayloadOffset);
