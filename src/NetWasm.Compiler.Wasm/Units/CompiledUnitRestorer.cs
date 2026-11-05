using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Units;

internal interface ICompiledUnitRestorer
{
    // The method's emission from its unit, or null when the unit is missing or stale.
    ManagedBodyEmission? Restore(
        string identity,
        string recordIdentity,
        StructuredMethod method,
        MethodRootMap rootMap,
        InstructionModuleTarget target);
}

/// <summary>
/// Reuses a method's unit when its emission input fingerprints the same, every query it
/// recorded answers the same, and every symbol its body calls exists in this module; the
/// body is then copied out of the segment with its indices rebound. The unit is read in
/// place: only the body and the names emission needs become managed values. A segment's
/// queries are checked once per compile, by whichever worker first needs each one. One per
/// worker: checking asks the worker's own providers.
/// </summary>
internal sealed class CompiledUnitRestorer(
    CompiledUnitState state,
    IUnitQueryReplayer replayer,
    IWasmIndexOperandRewriter rewriter) : ICompiledUnitRestorer
{
    private static readonly IdentityRemapping Identity = new();

    public ManagedBodyEmission? Restore(
        string identity,
        string recordIdentity,
        StructuredMethod method,
        MethodRootMap rootMap,
        InstructionModuleTarget target)
    {
        try
        {
            return RestoreUnit(identity, recordIdentity, method, rootMap, target);
        }
        catch (System.IO.InvalidDataException)
        {
            return Miss(ref state.CorruptUnits);
        }
    }

    private ManagedBodyEmission? RestoreUnit(
        string identity,
        string recordIdentity,
        StructuredMethod method,
        MethodRootMap rootMap,
        InstructionModuleTarget target)
    {
        if (state.Store.Find(identity) is not { } location)
            return Miss(ref state.MissingUnits);
        var segment = state.Store.Segments[location.Segment]!;
        var unit = segment.Record(location.Offset);
        if (state.InputOf(identity, rootMap) is not { } input || input != unit.Input)
            return Miss(ref state.InputChanged);
        var operands = ImmutableArray.CreateBuilder<WasmIndexOperand>(unit.RelocationCount);
        for (var index = 0; index < unit.RelocationCount; index++)
        {
            var relocation = unit.Relocation(index);
            if (!TryResolve(relocation.Space, unit.RelocationSymbol(index), out var resolved))
                return Miss(ref state.UnresolvedSymbols);
            operands.Add(new(relocation.Offset, relocation.Length, relocation.Space, resolved,
                relocation.IsSigned33));
        }
        UnitQueryArguments? arguments = null;
        for (var index = 0; index < unit.QueryCount; index++)
        {
            var id = unit.QueryId(index);
            var query = segment.Query(id);
            ref var slot = ref segment.QueryState(id);
            var status = Volatile.Read(ref slot);
            if (status == UnitSegment.Unknown)
            {
                status = Check(query, identity, method, target, ref arguments);
                Volatile.Write(ref slot, status);
            }
            switch (status)
            {
                case UnitSegment.Valid: continue;
                case UnitSegment.Unbuildable: return Miss(ref state.UnbuildableQueries);
                case UnitSegment.Failed: return Miss(ref state.ProviderFailures);
                default: return Miss(ref state.ChangedAnswers);
            }
        }
        var body = rewriter.Rewrite(unit.Body, operands.MoveToImmutable(), Identity);
        var parameters = ImmutableArray.CreateBuilder<CliValueKind>(unit.ParameterCount);
        for (var index = 0; index < unit.ParameterCount; index++) parameters.Add(unit.Parameter(index));
        var blocks = ImmutableDictionary.CreateBuilder<int, int>();
        for (var index = 0; index < unit.BlockCount; index++)
        {
            var (block, count) = unit.Block(index);
            blocks[block] = count;
        }
        var environmentKey = unit.EnvironmentKey.IsEmpty ? "" : unit.EnvironmentKey.ToString();
        var function = new WasmFunctionDefinition(
            unit.FunctionName.ToString(), new WasmFunctionType(parameters.MoveToImmutable(), unit.Result), body)
        {
            IsAggressiveInlining = unit.IsAggressiveInlining,
            IsNoInlining = unit.IsNoInlining,
        };
        var record = new ManagedMethodEmissionRecord(recordIdentity, environmentKey,
            new ManagedMethodBodyEmission(body, unit.InstructionCount, 0, null,
                environmentKey, FilterEnvironmentLayout.Empty, blocks.ToImmutable()));
        return new(function, environmentKey, FilterEnvironmentLayout.Empty, record)
        {
            Unit = location,
        };
    }

    // Whether a query's recorded answer holds in this compile. Its argument is rebuilt from
    // the identities this method names, then from the program's.
    private int Check(UnitQuery query, string identity, StructuredMethod method, InstructionModuleTarget target,
        ref UnitQueryArguments? arguments)
    {
        var key = query.Key.ToString();
        arguments ??= state.Bodies.TryGetValue(identity, out var body)
            ? UnitQueryArguments.Of(body)
            : UnitQueryArguments.Of(method);
        var answer = replayer.Replay(query.Kind, key, arguments, target);
        if (answer.Status == UnitQueryStatus.Unbuildable && state.Program is { } program)
            answer = replayer.Replay(query.Kind, key, arguments.Before(program.Value), target);
        switch (answer.Status)
        {
            case UnitQueryStatus.Answered:
                if (answer.Fingerprint == query.Answer)
                    return UnitSegment.Valid;
                state.ChangedKinds.AddOrUpdate(query.Kind, 1, static (_, count) => count + 1);
                return UnitSegment.Changed;
            case UnitQueryStatus.Unbuildable:
                state.UnbuildableKinds.AddOrUpdate(query.Kind, 1, static (_, count) => count + 1);
                if (state.UnbuildableKeys.Count < 8)
                {
                    state.UnbuildableKeys.TryAdd(
                        $"{query.Kind} {key} (asked by {method.Header.MethodInstance?.CanonicalName ?? method.Header.Method.Key.ToString()})", 0);
                }
                return UnitSegment.Unbuildable;
            default:
                return UnitSegment.Failed;
        }
    }

    private static ManagedBodyEmission? Miss(ref int reason)
    {
        Interlocked.Increment(ref reason);
        return null;
    }

    private bool TryResolve(WasmIndexSpace space, ReadOnlySpan<char> symbol, out uint index)
    {
        if (space == WasmIndexSpace.Function)
            return state.TryResolve(symbol, out index);
        // Fixed spaces are named by their index, which must not have moved.
        var separator = symbol.IndexOf(':');
        return uint.TryParse(symbol[(separator + 1)..], NumberStyles.None,
            CultureInfo.InvariantCulture, out index);
    }

    private sealed class IdentityRemapping : IWasmIndexRemapping
    {
        public uint Remap(WasmIndexSpace space, uint index) => index;
    }
}
