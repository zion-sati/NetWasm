using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// A compile's whole-program plans: its instruction target and runtime object layout, and
/// the program facts an edited method's own facts are derived from.
/// </summary>
internal sealed record UnitPlans(InstructionModuleTarget Target, IRuntimeObjectLayout RuntimeObjects, UnitProgramFacts Program)
{
    // The stack-trace numbering the compile used, when it wrote stack traces.
    public UnitStackTraces? StackTraces { get; init; }
}

/// <summary>
/// A compile's stack-trace numbering, for a later compile to renumber against its sources:
/// each shown method's id, each shown constructed method's definition, and the fingerprint
/// of the symbol table (<see cref="StackTraceSymbolTable.Fingerprint"/>).
/// </summary>
internal sealed record UnitStackTraces(
    IReadOnlyDictionary<EntityKey, int> DirectMethodIds,
    IReadOnlyDictionary<string, int> ConstructedMethodIds,
    IReadOnlyDictionary<string, EntityKey> ConstructedDefinitions,
    UnitFingerprint Table);

/// <summary>
/// What a compile's whole-program analysis found that a method analysed alone needs: the
/// allocated types its dispatch and type tests resolve against, every method's identity,
/// and the by-ref effects of methods that have any, with the methods each by-ref analysis
/// read effects from.
/// </summary>
internal sealed record UnitProgramFacts(
    ImmutableArray<CliTypeIdentity> AllocatedTypes,
    IReadOnlyDictionary<EntityKey, string> DirectMethods,
    ImmutableArray<string> ConstructedMethods,
    IReadOnlyDictionary<string, ManagedByRefMethodEffects> ByRefEffects,
    IReadOnlyDictionary<string, ImmutableArray<string>> ByRefCallees);

/// <summary>The runtime object offsets a compile laid out, as its plans keep them.</summary>
internal sealed record UnitRuntimeObjectLayout(
    int StringLengthOffset,
    int StringDataOffset,
    int ArrayLengthOffset,
    int ArrayDataPointerOffset,
    int ArrayElementTypeIdOffset,
    int DelegateTargetOffset,
    int DelegateMethodIdOffset,
    int DelegateInvocationListOffset,
    int DelegateInvocationCountOffset) : IRuntimeObjectLayout;

/// <summary>
/// A compile's whole-program plans as its instruction target holds them: the function
/// index map, interop imports, the module data a method's emission reads, host callbacks,
/// asynchronous bindings, delegate adapters, member execution, the runtime import selection,
/// finalization and the emission policy. The maps units record per key (call, dispatch and
/// type-test sites, root maps, by-ref effects, callable methods, delegate types, finalizable
/// types, static initializer guards and exception metadata) are left empty for a replay to
/// fill from the units' answers. A target with stack-trace methods or native imports or
/// callbacks is written as one a replay cannot rebuild.
/// </summary>
internal static class UnitPlanCodec
{
    private const uint Magic = 0x4C504E4E; // "NNPL"
    private const int Version = 9;

    public static byte[] Encode(InstructionModuleTarget target, IRuntimeObjectLayout runtimeObjects, UnitProgramFacts program)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(runtimeObjects);
        ArgumentNullException.ThrowIfNull(program);
        var writer = new UnitModelWriter();
        writer.Int(unchecked((int)Magic));
        writer.Int(Version);
        var stackTraces = target.StackTraceMethods;
        var replayable = target.NativeImports.IsEmpty && target.NativeCallbacks.Methods.IsEmpty;
        writer.Bool(replayable);
        if (!replayable) return writer.ToArray();

        Functions(writer, target.FunctionIndices);
        Interop(writer, target.InteropImports);
        Data(writer, target.ModuleData);
        Callbacks(writer, target.HostCallbacks);
        Async(writer, target.JavaScriptAsyncBindings);
        writer.Bool(!stackTraces.Symbols.IsEmpty);
        if (!stackTraces.Symbols.IsEmpty)
        {
            writer.Int(stackTraces.DirectMethodIds.Count);
            foreach (var (method, id) in stackTraces.DirectMethodIds.OrderBy(pair => pair.Value))
            {
                writer.Entity(method);
                writer.Int(id);
            }
            writer.Int(stackTraces.ConstructedMethodIds.Count);
            foreach (var (name, id) in stackTraces.ConstructedMethodIds.OrderBy(pair => pair.Value))
            {
                writer.String(name);
                writer.Int(id);
                writer.Entity(stackTraces.ConstructedDefinitions[name]);
            }
            var table = StackTraceSymbolTable.Fingerprint(stackTraces.Symbols, stackTraces.LocationSymbols);
            writer.Long((long)table.First);
            writer.Long((long)table.Second);
        }
        writer.Int(stackTraces.ExceptionTraceOffset);
        writer.Int(stackTraces.StringTypeId);
        Runtime(writer, target.RuntimeImportSelection);
        writer.Bool(target.Finalization.UsesExplicitFinalizationApi);
        Adapters(writer, target.ObjectArrayDelegateAdapters);
        Members(writer, target.MemberExecution);
        writer.Bool(target.CollectManagedMethodMemoryMetrics);
        writer.Bool(target.EnableAddressTakenAggregatePromotion);
        writer.Bool(target.EnableBranchValueFramePlacement);
        writer.Bool(target.EnableAggregateReferenceLanes);
        writer.Bool(target.EnableArrayMetadataCaching);
        writer.Bool(target.HoistArrayNullChecks);
        writer.Int((int)target.LoopRotation);
        writer.Bool(target.EliminateArrayStoreChecks);
        writer.Bool(target.EnableScalarCallPreservation);
        writer.Bool(target.InlineSmallBlockOperations);
        writer.Bool(target.InlineArrayData);
        writer.Bool(target.InlineObjectAllocation);
        // The shared object dispatch bodies a sharing call site calls, by the declaration it dispatches.
        writer.Int(target.SharedObjectDispatch.Count);
        foreach (var (declaration, body) in target.SharedObjectDispatch.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.String(declaration);
            writer.Method(body);
        }
        writer.Int(runtimeObjects.StringLengthOffset);
        writer.Int(runtimeObjects.StringDataOffset);
        writer.Int(runtimeObjects.ArrayLengthOffset);
        writer.Int(runtimeObjects.ArrayDataPointerOffset);
        writer.Int(runtimeObjects.ArrayElementTypeIdOffset);
        writer.Int(runtimeObjects.DelegateTargetOffset);
        writer.Int(runtimeObjects.DelegateMethodIdOffset);
        writer.Int(runtimeObjects.DelegateInvocationListOffset);
        writer.Int(runtimeObjects.DelegateInvocationCountOffset);
        Program(writer, program);
        return writer.ToArray();
    }

    // The plans written, with the per-key maps empty; null for bytes of another format and
    // for plans a replay cannot rebuild.
    public static UnitPlans? Decode(ReadOnlyMemory<byte> bytes, IMethodRepository methods, IFieldRepository fields)
    {
        if (bytes.Length < 9) return null;
        var reader = new UnitModelReader(bytes, methods, fields);
        if (reader.Int() != unchecked((int)Magic) || reader.Int() != Version || !reader.Bool()) return null;

        var functions = Functions(reader);
        var interop = Interop(reader);
        var data = Data(reader);
        var callbacks = Callbacks(reader);
        var async = Async(reader);
        UnitStackTraces? numbering = null;
        if (reader.Bool())
        {
            var count = reader.Int();
            var direct = new Dictionary<EntityKey, int>(count);
            for (var index = 0; index < count; index++)
            {
                var method = reader.Entity();
                direct.Add(method, reader.Int());
            }
            count = reader.Int();
            var constructed = new Dictionary<string, int>(count, StringComparer.Ordinal);
            var definitions = new Dictionary<string, EntityKey>(count, StringComparer.Ordinal);
            for (var index = 0; index < count; index++)
            {
                var name = reader.String()!;
                constructed.Add(name, reader.Int());
                definitions.Add(name, reader.Entity());
            }
            numbering = new(direct, constructed, definitions, new((ulong)reader.Long(), (ulong)reader.Long()));
        }
        var stackTraces = new StackTraceMethodPlan(
            ImmutableDictionary<EntityKey, int>.Empty, ImmutableDictionary<string, int>.Empty, [],
            ImmutableDictionary<int, ImmutableArray<StackTraceLocationSymbol>>.Empty, reader.Int(), reader.Int());
        var runtime = Runtime(reader);
        var finalization = new FinalizationPlan(ImmutableHashSet<int>.Empty, reader.Bool());
        var adapters = Adapters(reader);
        var members = Members(reader);
        var metrics = reader.Bool();
        var promotion = reader.Bool();
        var target = new InstructionModuleTarget(
            functions, interop, data,
            Lookups.NoEntries<string, DispatchCallSiteModel>(),
            Lookups.NoEntries<string, TypeTestSiteModel>(),
            Lookups.Whole<IReadOnlyDictionary<string, MethodInstanceModel>>(ImmutableDictionary<string, MethodInstanceModel>.Empty),
            Lookups.Whole<IReadOnlyList<CliTypeIdentity>>([]),
            callbacks, async, stackTraces, runtime, finalization,
            Lookups.NoEntries<Core.IntermediateRepresentation.Calls.ManagedCallSiteKey, Core.IntermediateRepresentation.Calls.ManagedCallSite>(),
            adapters, members, metrics, promotion)
        {
            EnableBranchValueFramePlacement = reader.Bool(),
            EnableAggregateReferenceLanes = reader.Bool(),
            EnableArrayMetadataCaching = reader.Bool(),
            HoistArrayNullChecks = reader.Bool(),
            LoopRotation = (LoopRotationPolicy)reader.Int(),
            EliminateArrayStoreChecks = reader.Bool(),
            EnableScalarCallPreservation = reader.Bool(),
            InlineSmallBlockOperations = reader.Bool(),
            InlineArrayData = reader.Bool(),
            InlineObjectAllocation = reader.Bool(),
            SharedObjectDispatch = SharedObjectDispatch(reader),
        };
        var runtimeObjects = new UnitRuntimeObjectLayout(reader.Int(), reader.Int(), reader.Int(), reader.Int(), reader.Int(),
            reader.Int(), reader.Int(), reader.Int(), reader.Int());
        var program = Program(reader);
        if (!reader.AtEnd) throw new InvalidDataException("A compile's kept plans are longer than they read.");
        return new(target, runtimeObjects, program) { StackTraces = numbering };
    }

    private static ImmutableDictionary<string, MethodInstanceModel> SharedObjectDispatch(UnitModelReader reader)
    {
        var count = reader.Int();
        var bodies = ImmutableDictionary.CreateBuilder<string, MethodInstanceModel>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var declaration = reader.String()!;
            bodies.Add(declaration, reader.Method());
        }
        return bodies.ToImmutable();
    }

    private static void Program(UnitModelWriter writer, UnitProgramFacts program)
    {
        writer.Int(program.AllocatedTypes.Length);
        foreach (var type in program.AllocatedTypes) writer.Type(type);
        writer.Int(program.DirectMethods.Count);
        foreach (var (key, identity) in program.DirectMethods.OrderBy(pair => pair.Value, StringComparer.Ordinal))
        {
            writer.Entity(key);
            writer.String(identity);
        }
        writer.Int(program.ConstructedMethods.Length);
        foreach (var identity in program.ConstructedMethods) writer.String(identity);
        writer.Int(program.ByRefEffects.Count);
        foreach (var (identity, effects) in program.ByRefEffects.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.String(identity);
            Arguments(writer, effects.WrittenArguments);
            Arguments(writer, effects.EscapedArguments);
            Arguments(writer, effects.ReturnedArgumentAliases);
        }
        writer.Int(program.ByRefCallees.Count);
        foreach (var (identity, callees) in program.ByRefCallees.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.String(identity);
            writer.Int(callees.Length);
            foreach (var callee in callees) writer.String(callee);
        }
    }

    private static UnitProgramFacts Program(UnitModelReader reader)
    {
        var count = reader.Int();
        var allocated = ImmutableArray.CreateBuilder<CliTypeIdentity>(count);
        for (var index = 0; index < count; index++) allocated.Add(reader.Type()!);
        count = reader.Int();
        var direct = new Dictionary<EntityKey, string>(count);
        for (var index = 0; index < count; index++)
        {
            var key = reader.Entity();
            direct.Add(key, reader.String()!);
        }
        count = reader.Int();
        var constructed = ImmutableArray.CreateBuilder<string>(count);
        for (var index = 0; index < count; index++) constructed.Add(reader.String()!);
        count = reader.Int();
        var effects = new Dictionary<string, ManagedByRefMethodEffects>(count, StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var identity = reader.String()!;
            var written = Arguments(reader);
            var escaped = Arguments(reader);
            effects.Add(identity, new(written, escaped, Arguments(reader)));
        }
        count = reader.Int();
        var callees = new Dictionary<string, ImmutableArray<string>>(count, StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var identity = reader.String()!;
            var calleeCount = reader.Int();
            var list = ImmutableArray.CreateBuilder<string>(calleeCount);
            for (var callee = 0; callee < calleeCount; callee++) list.Add(reader.String()!);
            callees.Add(identity, list.MoveToImmutable());
        }
        return new(allocated.MoveToImmutable(), direct, constructed.MoveToImmutable(), effects, callees);
    }

    private static void Arguments(UnitModelWriter writer, ImmutableHashSet<int> arguments)
    {
        writer.Int(arguments.Count);
        foreach (var argument in arguments.Order()) writer.Int(argument);
    }

    private static ImmutableHashSet<int> Arguments(UnitModelReader reader)
    {
        var count = reader.Int();
        var arguments = ImmutableHashSet.CreateBuilder<int>();
        for (var index = 0; index < count; index++) arguments.Add(reader.Int());
        return arguments.ToImmutable();
    }

    private static void Functions(UnitModelWriter writer, FunctionIndexMap map)
    {
        Keyed(writer, map.DirectMethods);
        Named(writer, map.ConstructedMethods.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Value)));
        Named(writer, map.DelegateInvokeHelpers.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Value)));
        Keyed(writer, map.ImportedMethods);
        Optional(writer, map.ManagedThrowFunctionIndex);
        Optional(writer, map.ManagedRethrowFunctionIndex);
        Keyed(writer, map.JSImportForwarders);
        Named(writer, map.DelegateMulticastHelpers.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Value)));
        Keyed(writer, map.DelegateInvokeMethods);
        Named(writer, map.ConstructedDelegateInvokeMethods.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Value)));
    }

    private static FunctionIndexMap Functions(UnitModelReader reader)
    {
        var direct = Keyed(reader);
        var constructed = Named(reader).ToImmutableDictionary(pair => pair.Key, pair => new WasmFunctionIndex(pair.Value), StringComparer.Ordinal);
        var helpers = Named(reader).ToImmutableDictionary(pair => pair.Key, pair => new WasmFunctionIndex(pair.Value), StringComparer.Ordinal);
        var imported = Keyed(reader);
        return new FunctionIndexMap(direct, constructed, helpers, imported)
        {
            ManagedThrowFunctionIndex = Optional(reader),
            ManagedRethrowFunctionIndex = Optional(reader),
            JSImportForwarders = Keyed(reader),
            DelegateMulticastHelpers = Named(reader).ToImmutableDictionary(
                pair => pair.Key, pair => new WasmFunctionIndex(pair.Value), StringComparer.Ordinal),
            DelegateInvokeMethods = Keyed(reader),
            ConstructedDelegateInvokeMethods = Named(reader).ToImmutableDictionary(
                pair => pair.Key, pair => new WasmFunctionIndex(pair.Value), StringComparer.Ordinal),
        };
    }

    // Entries in key order, so equal maps encode alike.
    private static void Keyed(UnitModelWriter writer, ImmutableDictionary<EntityKey, WasmFunctionIndex> map)
    {
        writer.Int(map.Count);
        foreach (var (key, index) in map.OrderBy(pair => pair.Key.Assembly.Name, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key.MetadataToken))
        {
            writer.Entity(key);
            writer.Int(index.Value);
        }
    }

    private static ImmutableDictionary<EntityKey, WasmFunctionIndex> Keyed(UnitModelReader reader)
    {
        var count = reader.Int();
        var map = ImmutableDictionary.CreateBuilder<EntityKey, WasmFunctionIndex>();
        for (var index = 0; index < count; index++)
        {
            var key = reader.Entity();
            map.Add(key, new(reader.Int()));
        }
        return map.ToImmutable();
    }

    private static void Named(UnitModelWriter writer, IEnumerable<KeyValuePair<string, int>> entries)
    {
        var sorted = entries.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        writer.Int(sorted.Length);
        foreach (var (key, value) in sorted)
        {
            writer.String(key);
            writer.Int(value);
        }
    }

    private static List<KeyValuePair<string, int>> Named(UnitModelReader reader)
    {
        var count = reader.Int();
        var entries = new List<KeyValuePair<string, int>>(count);
        for (var index = 0; index < count; index++)
        {
            var key = reader.String()!;
            entries.Add(KeyValuePair.Create(key, reader.Int()));
        }
        return entries;
    }

    private static void Optional(UnitModelWriter writer, OptionalFunctionIndex index) =>
        writer.Int(index.IsPresent ? index.Value : -1);

    private static OptionalFunctionIndex Optional(UnitModelReader reader) =>
        reader.Int() is var value and >= 0 ? OptionalFunctionIndex.At(value) : OptionalFunctionIndex.Missing;

    private static void Interop(UnitModelWriter writer, InteropImportPlan plan)
    {
        writer.Int(plan.Imports.Length);
        foreach (var import in plan.Imports) Import(writer, import);
        Optional(writer, plan.StringLength);
        Optional(writer, plan.CopyStringUtf16);
        Optional(writer, plan.ReleaseHandle);
        Optional(writer, plan.ReleaseSubscription);
        Optional(writer, plan.ByteLength);
        Optional(writer, plan.CopyBytes);
        writer.Bool(plan.UseJavaScriptExportBoundary);
        Named(writer, plan.Conversions);
    }

    private static InteropImportPlan Interop(UnitModelReader reader)
    {
        var count = reader.Int();
        var imports = ImmutableArray.CreateBuilder<WasmFunctionImport>(count);
        for (var index = 0; index < count; index++) imports.Add(Import(reader));
        return new InteropImportPlan(imports.MoveToImmutable(), Optional(reader), Optional(reader), Optional(reader),
            Optional(reader), Optional(reader), Optional(reader), reader.Bool())
        {
            Conversions = Named(reader).ToImmutableDictionary(StringComparer.Ordinal),
        };
    }

    private static void Import(UnitModelWriter writer, WasmFunctionImport import)
    {
        writer.String(import.Module);
        writer.String(import.Name);
        writer.Int(import.Type.Parameters.Length);
        foreach (var parameter in import.Type.Parameters) writer.Byte((byte)parameter);
        writer.Byte((byte)import.Type.Result);
    }

    private static WasmFunctionImport Import(UnitModelReader reader)
    {
        var module = reader.String()!;
        var name = reader.String()!;
        var count = reader.Int();
        var parameters = ImmutableArray.CreateBuilder<CliValueKind>(count);
        for (var index = 0; index < count; index++) parameters.Add((CliValueKind)reader.Byte());
        return new WasmFunctionImport(module, name, new WasmFunctionType(parameters.MoveToImmutable(), (CliValueKind)reader.Byte()));
    }

    // What a method's emission reads of the module data that units do not record per key.
    private static void Data(UnitModelWriter writer, ModuleDataPlan data)
    {
        writer.Int(data.StaticDataEnd);
        writer.Bool(data.StaticInitializerFunctions is not null);
        if (data.StaticInitializerFunctions is { } functions)
        {
            writer.Int(functions.Initializers.Length);
            foreach (var initializer in functions.Initializers)
            {
                writer.String(initializer.Key);
                writer.Int(initializer.FunctionIndex);
                writer.Int(initializer.InitializerIndex);
                writer.Int(initializer.GuardAddress);
            }
            writer.Int(functions.CatchMetadataAddress);
            writer.Int(functions.FailureFunctionIndex);
        }
        Address(writer, data.NativeCallbackReadinessAddress);
        Address(writer, data.StateRegistrationGuardAddress);
        Address(writer, data.SidecarDescriptorAddress);
        Address(writer, data.SidecarSlotAddress);
    }

    private static ModuleDataPlan Data(UnitModelReader reader)
    {
        var end = reader.Int();
        StaticInitializerFunctionPlan? functions = null;
        if (reader.Bool())
        {
            var count = reader.Int();
            var initializers = ImmutableArray.CreateBuilder<StaticInitializerFunction>(count);
            for (var index = 0; index < count; index++)
                initializers.Add(new(reader.String()!, reader.Int(), reader.Int(), reader.Int()));
            functions = new(initializers.MoveToImmutable(), reader.Int(), reader.Int());
        }
        return new ModuleDataPlan(
            ImmutableDictionary<StructuredExceptionGroupKey, ExceptionGroupMetadata>.Empty, [], [],
            ImmutableDictionary<string, StaticInitializerGuard>.Empty, end)
        {
            StaticInitializerFunctions = functions,
            NativeCallbackReadinessAddress = Address(reader),
            StateRegistrationGuardAddress = Address(reader),
            SidecarDescriptorAddress = Address(reader),
            SidecarSlotAddress = Address(reader),
        };
    }

    private static void Address(UnitModelWriter writer, int? address)
    {
        writer.Bool(address is not null);
        writer.Int(address ?? 0);
    }

    private static int? Address(UnitModelReader reader)
    {
        var present = reader.Bool();
        var value = reader.Int();
        return present ? value : null;
    }

    private static void Callbacks(UnitModelWriter writer,
        ImmutableDictionary<(EntityKey Method, int ParameterIndex), HostCallbackDeclaration> callbacks)
    {
        writer.Int(callbacks.Count);
        foreach (var (key, callback) in callbacks.OrderBy(pair => pair.Key.Method.Assembly.Name, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key.Method.MetadataToken).ThenBy(pair => pair.Key.ParameterIndex))
        {
            writer.Entity(key.Method);
            writer.Int(key.ParameterIndex);
            writer.Entity(callback.ImportMethod);
            writer.Int(callback.ParameterIndex);
            writer.Method(callback.Invoke);
            writer.String(callback.ExportName);
        }
    }

    private static ImmutableDictionary<(EntityKey Method, int ParameterIndex), HostCallbackDeclaration> Callbacks(
        UnitModelReader reader)
    {
        var count = reader.Int();
        var callbacks = ImmutableDictionary.CreateBuilder<(EntityKey Method, int ParameterIndex), HostCallbackDeclaration>();
        for (var index = 0; index < count; index++)
        {
            var method = reader.Entity();
            var parameter = reader.Int();
            var import = reader.Entity();
            var importParameter = reader.Int();
            var invoke = reader.Method();
            callbacks.Add((method, parameter), new(import, importParameter, invoke, reader.String()!));
        }
        return callbacks.ToImmutable();
    }

    private static void Async(UnitModelWriter writer, IReadOnlyDictionary<EntityKey, JavaScriptAsyncMethodBinding> bindings)
    {
        writer.Int(bindings.Count);
        foreach (var (key, binding) in bindings.OrderBy(pair => pair.Key.Assembly.Name, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key.MetadataToken))
        {
            writer.Entity(key);
            writer.Entity(binding.Method);
            writer.Byte((byte)binding.Return.Kind);
            writer.Type(binding.Return.ResultType);
            writer.Type(binding.TaskType);
            writer.Method(binding.SetResult);
            writer.Method(binding.SetException);
            writer.Method(binding.SetCanceled);
            OptionalField(writer, binding.ValueTaskTaskField);
            OptionalMethod(writer, binding.ValueTaskAsTask);
            OptionalField(writer, binding.StatusField);
            OptionalField(writer, binding.ResultField);
            OptionalMethod(writer, binding.GetVoidResult);
            writer.Bool(binding.CommandEntry is not null);
            if (binding.CommandEntry is { } commandEntry)
            {
                writer.Method(commandEntry.Start);
                writer.Method(commandEntry.Callback);
                writer.Method(commandEntry.ExitCode);
            }
        }
    }

    private static ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding> Async(UnitModelReader reader)
    {
        var count = reader.Int();
        var bindings = ImmutableDictionary.CreateBuilder<EntityKey, JavaScriptAsyncMethodBinding>();
        for (var index = 0; index < count; index++)
        {
            var key = reader.Entity();
            var method = reader.Entity();
            var kind = (JavaScriptAsyncReturnKind)reader.Byte();
            var result = reader.Type();
            var task = reader.Type()!;
            var setResult = reader.Method();
            var setException = reader.Method();
            var setCanceled = reader.Method();
            bindings.Add(key, new JavaScriptAsyncMethodBinding(method, new(kind, result), task, setResult, setException, setCanceled)
            {
                ValueTaskTaskField = OptionalField(reader),
                ValueTaskAsTask = OptionalMethod(reader),
                StatusField = OptionalField(reader)!,
                ResultField = OptionalField(reader),
                GetVoidResult = OptionalMethod(reader),
                CommandEntry = reader.Bool()
                    ? new(reader.Method(), reader.Method(), reader.Method())
                    : null,
            });
        }
        return bindings.ToImmutable();
    }

    private static void Runtime(UnitModelWriter writer, RuntimeImportSelection selection)
    {
        writer.Byte((byte)selection.ModuleProfile);
        writer.Bool(selection.IncludeTerminalExceptionReporter);
        writer.Bool(selection.IncludeStackTrace);
        writer.Bool(selection.IncludeExceptionCapture);
        writer.Bool(selection.IncludeEphemeronHandles);
        writer.Bool(selection.IncludeTerminalExceptionRaise);
        writer.Bool(selection.IncludeFinalization);
        writer.Bool(selection.IncludeWeakHandles);
        writer.Bool(selection.IncludeRandomSeed);
        writer.Bool(selection.IncludeArraySetValue);
        writer.Bool(selection.IncludeUnhandledExceptionHint);
        writer.Bool(selection.IncludeObjectClone);
    }

    private static RuntimeImportSelection Runtime(UnitModelReader reader) => new(
        (WasmModuleProfile)reader.Byte(), reader.Bool(), reader.Bool(), reader.Bool(), reader.Bool(), reader.Bool(),
        reader.Bool(), reader.Bool(), reader.Bool(), reader.Bool(), reader.Bool(), reader.Bool());

    private static void Adapters(UnitModelWriter writer, ImmutableDictionary<string, ObjectArrayDelegateAdapterPlan> adapters)
    {
        writer.Int(adapters.Count);
        foreach (var (key, adapter) in adapters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.String(key);
            writer.Method(adapter.Factory);
            writer.Type(adapter.DelegateType);
            OptionalMethod(writer, adapter.Invoke);
            writer.Method(adapter.Target);
            writer.Bool(adapter.IsSupported);
        }
    }

    private static ImmutableDictionary<string, ObjectArrayDelegateAdapterPlan> Adapters(UnitModelReader reader)
    {
        var count = reader.Int();
        var adapters = ImmutableDictionary.CreateBuilder<string, ObjectArrayDelegateAdapterPlan>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var key = reader.String()!;
            var factory = reader.Method();
            var type = reader.Type()!;
            var invoke = OptionalMethod(reader);
            var target = reader.Method();
            adapters.Add(key, new(factory, type, invoke, target, reader.Bool()));
        }
        return adapters.ToImmutable();
    }

    private static void Members(UnitModelWriter writer, MemberExecutionPlan plan)
    {
        writer.Int(plan.Methods.Count);
        foreach (var (key, method) in plan.Methods.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.String(key);
            writer.Method(method.Descriptor);
            writer.Bool(method.RequiresDispatch);
            Targets(writer, method.Targets);
        }
        writer.Int(plan.Fields.Count);
        foreach (var (key, field) in plan.Fields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.String(key);
            writer.Field(field);
        }
        OptionalMethod(writer, plan.UnsupportedTarget);
        Entities(writer, plan.MethodInvokers);
        writer.Bool(plan.DelegateInvocation is not null);
        if (plan.DelegateInvocation is not { } invocation) return;
        writer.Int(invocation.Methods.Count);
        foreach (var (key, method) in invocation.Methods.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.String(key);
            writer.Method(method);
        }
        Entities(writer, invocation.Invokers);
        writer.Method(invocation.UnsupportedTarget);
        writer.Method(invocation.ArgumentTarget);
        writer.Method(invocation.ParameterCountTarget);
        writer.Method(invocation.InvocationTarget);
    }

    private static MemberExecutionPlan Members(UnitModelReader reader)
    {
        var count = reader.Int();
        var methods = ImmutableDictionary.CreateBuilder<string, MemberMethodExecutionPlan>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var key = reader.String()!;
            var descriptor = reader.Method();
            var dispatch = reader.Bool();
            methods.Add(key, new(descriptor, dispatch, Targets(reader)));
        }
        count = reader.Int();
        var fields = ImmutableDictionary.CreateBuilder<string, FieldInstanceModel>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var key = reader.String()!;
            fields.Add(key, reader.Field());
        }
        var unsupported = OptionalMethod(reader);
        var invokers = Entities(reader);
        DelegateDynamicInvokePlan? invocation = null;
        if (reader.Bool())
        {
            count = reader.Int();
            var invoked = ImmutableDictionary.CreateBuilder<string, MethodInstanceModel>(StringComparer.Ordinal);
            for (var index = 0; index < count; index++)
            {
                var key = reader.String()!;
                invoked.Add(key, reader.Method());
            }
            var invocationInvokers = Entities(reader);
            invocation = new(invoked.ToImmutable(), invocationInvokers, reader.Method(), reader.Method(), reader.Method(),
                reader.Method());
        }
        return new MemberExecutionPlan(methods.ToImmutable(), fields.ToImmutable(), unsupported)
        {
            MethodInvokers = invokers,
            DelegateInvocation = invocation,
        };
    }

    private static void Targets(UnitModelWriter writer, ImmutableArray<DispatchTargetModel> targets)
    {
        writer.Int(targets.Length);
        foreach (var target in targets)
        {
            writer.Type(target.ReceiverType);
            writer.Method(target.Method);
        }
    }

    private static ImmutableArray<DispatchTargetModel> Targets(UnitModelReader reader)
    {
        var count = reader.Int();
        var targets = ImmutableArray.CreateBuilder<DispatchTargetModel>(count);
        for (var index = 0; index < count; index++)
        {
            var receiver = reader.Type()!;
            targets.Add(new(receiver, reader.Method()));
        }
        return targets.MoveToImmutable();
    }

    private static void Entities(UnitModelWriter writer, ImmutableHashSet<EntityKey> keys)
    {
        writer.Int(keys.Count);
        foreach (var key in keys.OrderBy(key => key.Assembly.Name, StringComparer.Ordinal).ThenBy(key => key.MetadataToken))
            writer.Entity(key);
    }

    private static ImmutableHashSet<EntityKey> Entities(UnitModelReader reader)
    {
        var count = reader.Int();
        var keys = ImmutableHashSet.CreateBuilder<EntityKey>();
        for (var index = 0; index < count; index++) keys.Add(reader.Entity());
        return keys.ToImmutable();
    }

    private static void OptionalMethod(UnitModelWriter writer, MethodInstanceModel? method)
    {
        writer.Bool(method is not null);
        if (method is not null) writer.Method(method);
    }

    private static MethodInstanceModel? OptionalMethod(UnitModelReader reader) => reader.Bool() ? reader.Method() : null;

    private static void OptionalField(UnitModelWriter writer, FieldInstanceModel? field)
    {
        writer.Bool(field is not null);
        if (field is not null) writer.Field(field);
    }

    private static FieldInstanceModel? OptionalField(UnitModelReader reader) => reader.Bool() ? reader.Field() : null;
}
