using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class CorpusExportFactory : ICorpusExportFactory
{
    public ImmutableArray<RequestedExport> Create(
            CorpusFixture fixture)
    {
        var exports = ImmutableArray.CreateBuilder<RequestedExport>();
        if (fixture.RequiresReactor)
        {
            exports.Add(new RequestedExport(
                "run",
                fixture.EntryType,
                fixture.WasmEntryMethod));
        }

        if (fixture.ReactorObserveMethod is not null)
        {
            exports.Add(new RequestedExport(
                "observe",
                fixture.EntryType,
                fixture.ReactorObserveMethod));
        }

        if (fixture.ExposesLegacyTrace)
        {
            exports.Add(new("trace", fixture.EntryType, "Trace"));
        }
        if (fixture.UsesTypedTrace)
        {
            exports.Add(new("trace_count", fixture.EntryType, "TraceCount"));
            exports.Add(new("trace_kind", fixture.EntryType, "TraceKind"));
            exports.Add(new("trace_event_id", fixture.EntryType, "TraceEventId"));
            exports.Add(new("trace_payload_low", fixture.EntryType, "TracePayloadLow"));
            exports.Add(new("trace_payload_high", fixture.EntryType, "TracePayloadHigh"));
        }
        return exports.ToImmutable();
    }
}
