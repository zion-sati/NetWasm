using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission;

public enum ManagedBoundaryKind
{
    ProcessEntryPoint,
    SynchronousExport,
    HostCallback,
    AsynchronousExportStart,
    AsynchronousExportCompletion,
    AsynchronousExportObservation,
    AsynchronousProcessStart,
    AsynchronousProcessCompletion,
    AsynchronousProcessObservation,
    AsynchronousImportCompletion,
    ComponentAdapter,
    ComponentInitialize,
    ComponentReallocate,
    ComponentPostReturn,
    InternalRuntimeDispatch,
}

public enum ManagedBoundaryFailureDisposition
{
    ReportAndTerminate,
    RejectAsynchronousOperation,
    FailComponentOperation,
    PropagateManagedException,
}

public sealed class ManagedBoundaryFailurePolicy :
    IManagedBoundaryFailureDispositionResolver
{
    private readonly ImmutableDictionary<ManagedBoundaryKind, ManagedBoundaryFailureDisposition> _dispositions =
        new Dictionary<ManagedBoundaryKind, ManagedBoundaryFailureDisposition>
        {
            [ManagedBoundaryKind.ProcessEntryPoint] = ManagedBoundaryFailureDisposition.ReportAndTerminate,
            [ManagedBoundaryKind.SynchronousExport] = ManagedBoundaryFailureDisposition.ReportAndTerminate,
            [ManagedBoundaryKind.HostCallback] = ManagedBoundaryFailureDisposition.ReportAndTerminate,
            [ManagedBoundaryKind.AsynchronousExportStart] = ManagedBoundaryFailureDisposition.RejectAsynchronousOperation,
            [ManagedBoundaryKind.AsynchronousExportCompletion] = ManagedBoundaryFailureDisposition.RejectAsynchronousOperation,
            [ManagedBoundaryKind.AsynchronousExportObservation] = ManagedBoundaryFailureDisposition.RejectAsynchronousOperation,
            [ManagedBoundaryKind.AsynchronousProcessStart] = ManagedBoundaryFailureDisposition.RejectAsynchronousOperation,
            [ManagedBoundaryKind.AsynchronousProcessCompletion] = ManagedBoundaryFailureDisposition.RejectAsynchronousOperation,
            [ManagedBoundaryKind.AsynchronousProcessObservation] = ManagedBoundaryFailureDisposition.RejectAsynchronousOperation,
            [ManagedBoundaryKind.AsynchronousImportCompletion] = ManagedBoundaryFailureDisposition.PropagateManagedException,
            [ManagedBoundaryKind.ComponentAdapter] = ManagedBoundaryFailureDisposition.FailComponentOperation,
            [ManagedBoundaryKind.ComponentInitialize] = ManagedBoundaryFailureDisposition.FailComponentOperation,
            [ManagedBoundaryKind.ComponentReallocate] = ManagedBoundaryFailureDisposition.FailComponentOperation,
            [ManagedBoundaryKind.ComponentPostReturn] = ManagedBoundaryFailureDisposition.FailComponentOperation,
            [ManagedBoundaryKind.InternalRuntimeDispatch] = ManagedBoundaryFailureDisposition.PropagateManagedException,
        }.ToImmutableDictionary();

    public ManagedBoundaryFailureDisposition Resolve(ManagedBoundaryKind kind)
    {
        if (!_dispositions.TryGetValue(kind, out var disposition))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown managed boundary kind.");

        return disposition;
    }
}
