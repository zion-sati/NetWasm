using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// A method's view of the module target whose keyed maps record each entry the method
/// looks up: call sites, callees' by-ref effects, dispatch and type-test sites and root maps.
/// </summary>
internal static class UnitTargetViews
{
    public static InstructionModuleTarget Recording(
        InstructionModuleTarget target, IUnitDependencyRecorder recorder)
    {
        // Every method's emission depends on the target's whole-program plans.
        recorder.Record(UnitQueryKind.TargetPlans, "", UnitTargetFingerprint.Of(target), target);
        return View(target, new RecordingTargetMaps(recorder));
    }

    // The function indices a method's emission writes as values are recorded; call
    // operands are rebound by symbol.
    public static IFunctionIndexResolver Recording(IFunctionIndexResolver indices, IUnitDependencyRecorder recorder) =>
        new RecordingFunctionIndexResolver(indices, recorder);

    // A function address's key: a constructed method by canonical name, a definition by key.
    public static string FunctionAddressKey(MethodInstanceModel method) => method.IsConstructed
        ? "c:" + method.CanonicalName
        : "d:" + UnitQueryKeys.Of(method.Definition.Key);

    public static UnitFingerprint FunctionAddressAnswer(bool found, int index)
    {
        var answer = new UnitFingerprintBuilder();
        answer.Add(found);
        answer.Add(found ? index : -1);
        return answer.Finish();
    }

    public static UnitFingerprint CallableMethodsAnswer(IReadOnlyDictionary<string, MethodInstanceModel> methods)
    {
        var answer = new UnitFingerprintBuilder();
        answer.Add(methods.Count);
        foreach (var (key, method) in methods.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            answer.Add(key);
            answer.Add(method.CanonicalName);
            answer.Add(method.Definition.IsStatic);
            answer.Add(method.Signature.ReturnSignatureType);
            answer.Add(method.Signature.ParameterSignatureTypes.Length);
            foreach (var parameter in method.Signature.ParameterSignatureTypes) answer.Add(parameter);
        }
        return answer.Finish();
    }

    public static UnitFingerprint DelegateTypesAnswer(IReadOnlyList<CliTypeIdentity> types)
    {
        var answer = new UnitFingerprintBuilder();
        answer.Add(types.Count);
        foreach (var type in types) answer.Add(type);
        return answer.Finish();
    }

    // The target as a compile that replays the last one's answers sees it: its plans as kept,
    // and each map answered from the units' answers.
    public static InstructionModuleTarget Replay(InstructionModuleTarget plans, UnitAnswers answers) =>
        View(plans, new ReplayTargetMaps(answers));

    // Every map of the target a method's emission looks entries up in, with the query that
    // records or replays each lookup.
    private static InstructionModuleTarget View(InstructionModuleTarget target, IUnitTargetMaps maps) => target with
    {
        CallableMethods = maps.Answer(target.CallableMethods, UnitQueryKind.CallableMethods, CallableMethodsAnswer),
        DelegateTypes = maps.Answer(target.DelegateTypes, UnitQueryKind.DelegateTypes, DelegateTypesAnswer),
        FinalizableTypeIds = maps.Answer(target.FinalizableTypeIds, UnitQueryKind.Finalizable,
            static id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ManagedCallSites = maps.Answer(target.ManagedCallSites, UnitQueryKind.ManagedCallSite,
            static key => $"{key.Caller.CanonicalName}@{key.InstructionOffset:x8}",
            static (ref answer, site) => answer.Add(site)),
        DispatchCallSites = maps.Answer(target.DispatchCallSites, UnitQueryKind.DispatchCallSite,
            static key => key, static (ref answer, site) => answer.Add(site)),
        TypeTestSites = maps.Answer(target.TypeTestSites, UnitQueryKind.TypeTestSite,
            static key => key, static (ref answer, site) => answer.Add(site)),
        ManagedByRefEffects = maps.Answer(target.ManagedByRefEffects, UnitQueryKind.ByRefEffects,
            static key => key, static (ref answer, effects) => answer.Add(effects)),
        RootMaps = maps.Answer(target.RootMaps, UnitQueryKind.RootMap,
            static key => key, static (ref answer, map) => answer.Add(map)),
        StackTraceLookups = new(
            maps.Answer(target.StackTraceLookups.DirectMethodIds, UnitQueryKind.StackTraceDirectMethodId,
                UnitQueryKeys.Of, static (ref answer, id) => answer.Add(id)),
            maps.Answer(target.StackTraceLookups.ConstructedMethodIds, UnitQueryKind.StackTraceConstructedMethodId,
                static key => key, static (ref answer, id) => answer.Add(id)),
            maps.Answer(target.StackTraceLookups.LocationSymbols, UnitQueryKind.StackTraceLocationSymbols,
                static key => key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                static (ref answer, symbols) =>
                {
                    answer.Add(symbols.Length);
                    foreach (var symbol in symbols)
                    {
                        answer.Add(symbol.Offset);
                        answer.Add(symbol.SymbolId);
                    }
                })),
        StaticInitializerGuards = maps.Answer(target.StaticInitializerGuards, UnitQueryKind.StaticInitializerGuard,
            static key => key, static (ref answer, guard) =>
            {
                // The guard's function index is relocated by symbol like any call.
                answer.Add(guard.Address);
                answer.Add(guard.Direct is { } direct ? UnitQueryKeys.Of(direct) : null);
                answer.Add(guard.Constructed);
            }),
        ExceptionMetadata = maps.Answer(target.ExceptionMetadata, UnitQueryKind.ExceptionMetadata,
            ExceptionGroupKeyText, static (ref answer, metadata) =>
            {
                answer.Add(metadata.Address);
                answer.Add(metadata.ClauseCount);
                answer.Add(metadata.HasFilters);
            }),
    };

    // An exception group's key as text: method, instance name and group id, separated by a
    // character no name holds.
    public static string ExceptionGroupKeyText(StructuredExceptionGroupKey key) =>
        $"{UnitQueryKeys.Of(key.Method)}\u001F{key.MethodInstanceCanonicalName}\u001F{key.Group.Value}";

    public static bool TryParseExceptionGroupKey(string text, out StructuredExceptionGroupKey key)
    {
        key = default;
        var parts = text.Split('\u001F');
        var separator = parts.Length == 3 ? parts[0].LastIndexOf(":0x", System.StringComparison.Ordinal) : -1;
        if (separator < 0 ||
            !int.TryParse(parts[0].AsSpan(separator + 3), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var token) ||
            !int.TryParse(parts[2], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var group))
        {
            return false;
        }
        key = new(new EntityKey(new AssemblyIdentity(parts[0][..separator]), token),
            parts[1].Length == 0 ? null : parts[1], new StructuredExceptionGroupId(group));
        return true;
    }
}

/// <summary>
/// A function index resolver that records each index emission writes as a value. A unit
/// holding one is reused only while the function keeps its index.
/// </summary>
internal sealed class RecordingFunctionIndexResolver(
    IFunctionIndexResolver inner,
    IUnitDependencyRecorder recorder) : IFunctionIndexResolver
{
    public OptionalFunctionIndex ManagedThrowFunction => inner.ManagedThrowFunction;

    public int Resolve(EntityKey method) => inner.Resolve(method);

    public int Resolve(string method) => inner.Resolve(method);

    public int Resolve(MethodInstanceModel method) => inner.Resolve(method);

    public int Resolve(InteropImportKey import) => inner.Resolve(import);

    public int Resolve(FunctionAddress address)
    {
        var index = inner.Resolve(address);
        recorder.Record(UnitQueryKind.FunctionAddress, UnitTargetViews.FunctionAddressKey(address.Method),
            UnitTargetViews.FunctionAddressAnswer(true, index), new UnitLookup<int>(true, index));
        return index;
    }
}
