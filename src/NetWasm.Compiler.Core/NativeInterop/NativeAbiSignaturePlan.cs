using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.NativeInterop;

public sealed record NativeAbiSignaturePlan(
    MethodSignatureModel LogicalSignature,
    MethodSignatureModel PhysicalSignature,
    ImmutableArray<NativeAbiParameterPlan> Parameters,
    NativeAbiValuePlan Result,
    int? HiddenResultParameterIndex);
