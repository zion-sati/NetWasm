using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record MethodEmissionContext(
    MethodRootMap RootMap,
    int LocalBase,
    int StackBase,
    EvaluationStackLocalLayout StackLocals,
    int ObjectTemporary,
    int RootFrame,
    int ExceptionTemporary,
    IReadOnlyDictionary<StructuredExceptionGroupId, int> ExceptionFrameLocals,
    IReadOnlyDictionary<StructuredExceptionGroupId, int> ExceptionContinuationLocals,
    int ValueFrame,
    ValueFrameLayout ValueLayout,
    int FilterRootFrame,
    FilterEnvironmentLayout FilterEnvironment,
    int ParameterOffset,
    int InteropDescriptor,
    int InteropResult,
    int InteropHandle,
    int NumericTemporaryI4,
    int NumericTemporaryI8,
    int DispatcherProgramCounter,
    bool LeaveFrameOnRethrow = true,
    bool LeaveFrameOnExceptionalExit = true,
    StructuredExceptionGroup? ActiveExceptionGroup = null,
    bool IsFilterFunclet = false,
    int StackTraceMethodId = 0,
    RuntimeImportSelection RuntimeImportSelection = default,
    ManagedMethodIdentity CallerIdentity = default)
{
    public int NumericTemporaryI4Second => checked(DispatcherProgramCounter + 1);
    public int NumericTemporaryI4Third => checked(DispatcherProgramCounter + 2);
    public int NumericTemporaryI4Fourth => checked(DispatcherProgramCounter + 3);
    public int NumericTemporaryI4Fifth => checked(DispatcherProgramCounter + 4);
}
