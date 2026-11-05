using System.Globalization;
using System.Threading;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// The last compile, as a compile that emits against it sees it: its plans, its function
/// indices and its units' answers in place of the whole-program analysis, layouts and
/// planning. A method emitted against it gets the body the last compile's module would
/// give it, while the program's whole-program state is the last compile's.
/// </summary>
internal sealed class UnitReplay(UnitAnswers answers, UnitPlans plans, IFunctionIndexResolver indices)
{
    private int _replayed;
    private int _missed;
    private string? _firstMiss;

    internal InstructionModuleTarget Target { get; } = UnitTargetViews.Replay(plans.Target, answers);

    internal IFunctionIndexResolver Indices { get; } = indices;

    internal ManagedLayoutFork Fork { get; } = new(new ReplayValueLayoutProvider(answers),
        new ReplayInstanceFieldLayoutProvider(answers));

    // The value layouts, as root-map analysis reads them too.
    internal IValueLayoutProvider Values => Fork.Values;

    // A worker's capabilities with the layouts answered from the last compile; the
    // metadata services are the compile's own.
    internal ManagedBodySharedCapabilities Capabilities(ManagedBodySharedCapabilities shared) => Capabilities(
        shared.Types, shared.TypeDefinitions, shared.Fields, shared.Methods, shared.Symbols, shared.TypeClassifier,
        shared.Intrinsics, shared.TargetLayout, shared.Logger, shared.Structuring);

    internal ManagedBodySharedCapabilities Capabilities(ITypeRepository types, ITypeDefinitionResolver typeDefinitions,
        IFieldRepository fields, IMethodRepository methods, ISymbolFormatter symbols, ITypeClassifier typeClassifier,
        IRuntimeIntrinsicRegistry intrinsics, ITargetLayout targetLayout,
        Microsoft.Extensions.Logging.ILogger<WasmModuleEmitterFactory> logger, IWasmMethodLowerer? structuring) => new(
        types, typeDefinitions, fields, methods, symbols, typeClassifier, intrinsics, targetLayout,
        new ReplayTypeLayoutProvider(answers), new ReplayStaticFieldLayoutProvider(answers),
        new ReplayStaticDataLayout(answers), new ReplayMemberDescriptorLayout(answers),
        plans.RuntimeObjects, new ReplayManagedExceptionObjectProvider(answers),
        new ReplayTypeDescriptorSource(answers), logger)
        {
            Structuring = structuring,
        };

    internal void Replayed() => Interlocked.Increment(ref _replayed);

    internal void Missed(UnitReplayMissException miss)
    {
        Interlocked.Increment(ref _missed);
        Interlocked.CompareExchange(ref _firstMiss, miss.Message, null);
    }

    internal string Describe() =>
        string.Create(CultureInfo.InvariantCulture,
            $"replayed {_replayed}, missed {_missed}{(_firstMiss is null ? "" : $" (first: {_firstMiss})")}; ");
}
