namespace NetWasm.Compiler.ControlFlow.Structuring;

internal readonly record struct StructuredSequenceStepResult(
    StructuredSequenceStepDisposition Disposition,
    int? NextBlockOffset);
