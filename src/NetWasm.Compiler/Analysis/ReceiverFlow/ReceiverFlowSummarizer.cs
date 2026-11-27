// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.ReceiverFlow;

internal interface IReceiverFlowSummarizer
{
    ReceiverFlowSummary Summarize(ManagedMethodBody body);
}

// What a method body contributes to the receiver flow graph (RECEIVER_TYPE_FLOW_DESIGN.md, 4.2 and 4.3): where its
// reference values come from and where they go. It runs the body block by block over a symbolic stack whose entries
// are the sources of each value; a block's entry stack is one node per slot, which its predecessors' exit stacks flow
// into. Anything it does not model pushes a top value bounded by what it knows of the value's type.
internal sealed class ReceiverFlowSummarizer(
    ICalledMethodResolver calls,
    ITypeOperandResolver typeOperands,
    IFieldRepository fields,
    IDelegateTypeRecognizer delegates,
    IDispatchSiteKeyBuilder siteKeys,
    Func<string, bool> isDispatchSite,
    IReachingLocalStores reachingStores,
    IRuntimeWrittenFields runtimeFields) : IReceiverFlowSummarizer
{
    // A string literal is a System.String, whether or not the closure has allocated one yet.
    private static readonly CliTypeIdentity String =
        CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);

    private readonly ICalledMethodResolver _calls = calls ?? throw new ArgumentNullException(nameof(calls));
    private readonly ITypeOperandResolver _typeOperands = typeOperands ?? throw new ArgumentNullException(nameof(typeOperands));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IDelegateTypeRecognizer _delegates = delegates ?? throw new ArgumentNullException(nameof(delegates));
    private readonly IDispatchSiteKeyBuilder _siteKeys = siteKeys ?? throw new ArgumentNullException(nameof(siteKeys));
    private readonly Func<string, bool> _isDispatchSite = isDispatchSite ?? throw new ArgumentNullException(nameof(isDispatchSite));
    private readonly IReachingLocalStores _reachingStores =
        reachingStores ?? throw new ArgumentNullException(nameof(reachingStores));
    private readonly IRuntimeWrittenFields _runtimeFields = runtimeFields ?? throw new ArgumentNullException(nameof(runtimeFields));

    public ReceiverFlowSummary Summarize(ManagedMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new Run(this, body).Summarize();
    }

    // A value on the symbolic stack: the sources of a reference, the node an address points at, or the methods a
    // function pointer may name. An empty value is null or not a reference.
    private sealed record Slot(ImmutableArray<FlowSource> Sources)
    {
        public static Slot Empty { get; } = new(ImmutableArray<FlowSource>.Empty);

        // An address of a local, argument or field the analysis has a node for.
        public FlowNodeKey? Address { get; init; }

        public bool ReferenceAddress { get; init; }

        public ImmutableArray<MethodInstanceModel> Functions { get; init; } = [];

        // The method a direct call this value is the result of called, for an await of it.
        public MethodInstanceModel? DirectCallee { get; init; }

        public static Slot Of(FlowSource source) => new([source]);

        public static Slot Top(CliTypeIdentity? bound, string cause) => Of(new FlowSource.Top(FlowBound.Of(bound, cause)));
    }

    private sealed class Run(ReceiverFlowSummarizer owner, ManagedMethodBody body)
    {
        private readonly List<FlowEdge> _edges = [];
        private readonly List<FlowSite> _sites = [];
        private readonly List<MethodInstanceModel> _reached = [];
        private readonly List<MethodInstanceModel> _addressEscapes = [];
        private readonly List<(CliTypeIdentity, FlowNodeKey)> _reinterpretations = [];
        private readonly List<Slot> _stack = [];
        private readonly string _caller = body.Method.CanonicalName;
        private CliTypeIdentity? _constrained;
        // An instruction whose stack effect is not known ran: no slot below the next validated height is known.
        private bool _unknownEffect;
        // For each load of a local followed store by store, the stores it reads; locals whose address the method takes
        // (and every local of a method too large) are followed by slot.
        private IReadOnlyDictionary<int, ImmutableArray<int>>? _reaching;
        private HashSet<int> _followed = [];

        internal ReceiverFlowSummary Summarize()
        {
            var graph = body.ControlFlow.Graph;
            var addressed = new HashSet<int>();
            foreach (var instruction in body.Body.Instructions)
            {
                if (instruction is { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } })
                    addressed.Add(local);
            }
            for (var local = 0; local < body.Body.LocalSignatureTypes.Length; local++)
            {
                if (!addressed.Contains(local) && IsReference(body.Body.LocalSignatureTypes[local])) _followed.Add(local);
            }
            _reaching = _followed.Count == 0 ? null : owner._reachingStores.Resolve(graph, _followed);
            if (_reaching is null) _followed = [];
            var entries = body.ControlFlow.InstructionEntryStacks;
            foreach (var block in graph.Blocks)
            {
                // A block no path from the entry or a handler reaches has no entry stack: it never runs, and the
                // closure reaches nothing it names (UnreachableCodeEraser).
                if (block.Instructions.IsEmpty ||
                    !entries.TryGetValue(block.Instructions[0].Offset, out var entry))
                {
                    continue;
                }
                _stack.Clear();
                _constrained = null;
                _unknownEffect = false;
                var handlerEntry = entry.Length > 0 && graph.Predecessors[block.Index].IsEmpty;
                for (var slot = 0; slot < entry.Length; slot++)
                {
                    var key = new FlowNodeKey(FlowNodeKind.StackEntry, _caller, block.Index, slot);
                    if (entry[slot] == CliValueKind.ManagedReference)
                    {
                        if (handlerEntry) Seed(key, null, "exception handler entry");
                        _stack.Add(Slot.Of(new FlowSource.Node(key, [])));
                    }
                    else
                    {
                        _stack.Add(Slot.Empty);
                    }
                }
                foreach (var instruction in block.Instructions)
                {
                    if (!entries.TryGetValue(instruction.Offset, out var expected)) continue;
                    Resynchronize(expected);
                    Execute(instruction);
                }
                foreach (var successor in graph.Successors[block.Index])
                {
                    var target = graph.Blocks[successor];
                    if (target.Instructions.IsEmpty ||
                        !entries.TryGetValue(target.Instructions[0].Offset, out var successorEntry))
                    {
                        continue;
                    }
                    for (var slot = 0; slot < successorEntry.Length; slot++)
                    {
                        if (successorEntry[slot] != CliValueKind.ManagedReference) continue;
                        var key = new FlowNodeKey(FlowNodeKind.StackEntry, _caller, successor, slot);
                        if (_unknownEffect) Seed(key, null, "unmodeled stack effect");
                        else if (_stack.Count == successorEntry.Length) Flow(_stack[slot], key, null);
                        else Seed(key, null, "stack height differs at a join");
                    }
                }
                // An address or function pointer left on the stack is not followed into another block: what it points at
                // may be written there (a cached delegate's dup and branch keeps a field's address across one).
                foreach (var left in _stack) Escape(left);
            }
            return new ReceiverFlowSummary(body.Method, Collapsed(), [.. _sites], [.. _reached], [.. _addressEscapes],
                [.. _reinterpretations]);
        }

        // A local's store or a block's entry slot fed by one node without a cast is that node: its edge and node go,
        // and what read it reads the node. Both are the method's own, so nothing outside the summary names them.
        private ImmutableArray<FlowEdge> Collapsed()
        {
            var incoming = new Dictionary<FlowNodeKey, int>();
            for (var index = 0; index < _edges.Count; index++)
            {
                if (_edges[index].Target.Kind is not (FlowNodeKind.LocalStore or FlowNodeKind.StackEntry)) continue;
                incoming[_edges[index].Target] = incoming.ContainsKey(_edges[index].Target) ? -1 : index;
            }
            var alias = new Dictionary<FlowNodeKey, FlowNodeKey>();
            foreach (var (key, index) in incoming)
            {
                if (index >= 0 && _edges[index].Source is FlowSource.Node { Casts.IsEmpty: true } source)
                    alias[key] = source.Key;
            }
            if (alias.Count == 0) return [.. _edges];
            var result = ImmutableArray.CreateBuilder<FlowEdge>(_edges.Count);
            foreach (var edge in _edges)
            {
                if (alias.ContainsKey(edge.Target) && incoming[edge.Target] >= 0 && Resolve(edge.Target, alias) != edge.Target)
                    continue;
                result.Add(edge.Source is FlowSource.Node node && alias.ContainsKey(node.Key)
                    ? edge with { Source = node with { Key = Resolve(node.Key, alias) } }
                    : edge);
            }
            return result.ToImmutable();
        }

        // The node an alias chain ends at; a chain that comes back to itself keeps its first node.
        private static FlowNodeKey Resolve(FlowNodeKey key, Dictionary<FlowNodeKey, FlowNodeKey> alias)
        {
            var seen = new HashSet<FlowNodeKey> { key };
            var current = key;
            while (alias.TryGetValue(current, out var next))
            {
                if (!seen.Add(next)) return key;
                current = next;
            }
            return current;
        }

        // The validated entry stack fixes the height where an instruction's effect was not modeled exactly.
        private void Resynchronize(ImmutableArray<CliValueKind> expected)
        {
            if (_unknownEffect)
            {
                while (_stack.Count > 0) Escape(Pop());
                _unknownEffect = false;
            }
            while (_stack.Count > expected.Length) Escape(Pop());
            while (_stack.Count < expected.Length)
            {
                _stack.Add(expected[_stack.Count] == CliValueKind.ManagedReference
                    ? Slot.Top(null, "unmodeled stack effect")
                    : Slot.Empty);
            }
        }

        private void Execute(CilInstruction instruction)
        {
            switch (instruction.Operation)
            {
                case CilOperation.Nop or CilOperation.Volatile or CilOperation.Readonly or CilOperation.Unaligned or
                    CilOperation.Break:
                    return;
                case CilOperation.Constrained:
                    _constrained = TypeOf(instruction);
                    return;
                case CilOperation.LoadNull or CilOperation.DefaultValue:
                    Push(Slot.Empty);
                    return;
                case CilOperation.LoadString:
                    Push(Slot.Of(new FlowSource.Object(new FlowObject.Exact(String))));
                    return;
                case CilOperation.LoadArgument when instruction.Operand is CilOperand.Index { Value: var argument }:
                    Push(Load(Parameter(argument), ParameterType(argument)));
                    return;
                case CilOperation.LoadArgumentAddress when instruction.Operand is CilOperand.Index { Value: var argument }:
                    Push(AddressOf(Parameter(argument), ParameterType(argument)));
                    return;
                case CilOperation.StoreArgument when instruction.Operand is CilOperand.Index { Value: var argument }:
                    Flow(Pop(), Parameter(argument), ParameterType(argument));
                    return;
                case CilOperation.LoadLocal when instruction.Operand is CilOperand.Index { Value: var local }:
                    if (_followed.Contains(local) && _reaching!.TryGetValue(instruction.Offset, out var stores))
                    {
                        Push(new Slot([.. stores.Select(store => (FlowSource)new FlowSource.Node(
                            new FlowNodeKey(FlowNodeKind.LocalStore, _caller, store), []))]));
                        return;
                    }
                    Push(Load(Local(local), LocalType(local)));
                    return;
                case CilOperation.LoadLocalAddress when instruction.Operand is CilOperand.Index { Value: var local }:
                    Push(AddressOf(Local(local), LocalType(local)));
                    return;
                case CilOperation.StoreLocal when instruction.Operand is CilOperand.Index { Value: var local }:
                    Flow(Pop(), _followed.Contains(local)
                        ? new FlowNodeKey(FlowNodeKind.LocalStore, _caller, instruction.Offset)
                        : Local(local), LocalType(local));
                    return;
                case CilOperation.Duplicate:
                    Push(_stack[^1]);
                    return;
                case CilOperation.Pop:
                    Pop();
                    return;
                case CilOperation.LoadField:
                    Pop();
                    Push(LoadField(instruction.Operand));
                    return;
                case CilOperation.LoadStaticField:
                    Push(LoadField(instruction.Operand));
                    return;
                case CilOperation.StoreField:
                {
                    var value = Pop();
                    Pop();
                    StoreField(instruction.Operand, value);
                    return;
                }
                case CilOperation.StoreStaticField:
                    StoreField(instruction.Operand, Pop());
                    return;
                case CilOperation.LoadFieldAddress:
                    Pop();
                    Push(FieldAddress(instruction.Operand));
                    return;
                case CilOperation.LoadStaticFieldAddress:
                    Push(FieldAddress(instruction.Operand));
                    return;
                case CilOperation.LoadObject:
                {
                    var address = Pop();
                    var type = TypeOf(instruction);
                    Push(type is not null && !IsReference(type) ? Slot.Empty
                        : address.Address is { } node ? Slot.Of(new FlowSource.Node(node, []))
                        : Slot.Top(type, "load through an address of unknown origin"));
                    return;
                }
                case CilOperation.StoreObject:
                {
                    var value = Pop();
                    var address = Pop();
                    if (address.Address is { } node) Flow(value, node, null);
                    else Escape(value);
                    return;
                }
                case CilOperation.CopyObject:
                {
                    var source = Pop();
                    var destination = Pop();
                    var type = TypeOf(instruction);
                    if (type is not null && IsReference(type) && destination.Address is { } node)
                    {
                        if (source.Address is { } from) _edges.Add(new(new FlowSource.Node(from, []), node, type));
                        else Seed(node, type, "copy from an address of unknown origin");
                    }
                    return;
                }
                case CilOperation.InitializeObject:
                    Pop();
                    return;
                case CilOperation.CompareExchange:
                {
                    var comparand = Pop();
                    var value = Pop();
                    var address = Pop();
                    Escape(comparand);
                    if (address.Address is { } node)
                    {
                        Flow(value, node, null);
                        Push(Slot.Of(new FlowSource.Node(node, [])));
                    }
                    else
                    {
                        Escape(value);
                        Push(Slot.Top(null, "compare-exchange through an address of unknown origin"));
                    }
                    return;
                }
                case CilOperation.Call or CilOperation.CallVirtual:
                    Call(instruction);
                    return;
                case CilOperation.NewObject:
                    NewObject(instruction);
                    return;
                case CilOperation.LoadFunction:
                    if (owner._calls.Resolve(instruction) is { } function)
                    {
                        Push(Slot.Empty with { Functions = [function] });
                        return;
                    }
                    break;
                // A virtual function's address names the override its receiver picks, which the closure may not have
                // resolved yet: a delegate made from it has a target the engine does not follow, and every such
                // target is a callable the engine treats as a boundary.
                case CilOperation.LoadVirtualFunction:
                    Pop();
                    Push(Slot.Empty);
                    return;
                case CilOperation.CastClass or CilOperation.IsInstance when TypeOf(instruction) is { } castType:
                    Push(Cast(Pop(), castType));
                    return;
                case CilOperation.UnboxAny when TypeOf(instruction) is { } unboxed:
                {
                    var value = Pop();
                    Push(IsReference(unboxed) ? Cast(value, unboxed) : Slot.Empty);
                    return;
                }
                case CilOperation.Box when TypeOf(instruction) is { } boxed:
                {
                    var value = Pop();
                    // A box of a reference type leaves the reference; a boxed Nullable<T> is a boxed T (or null).
                    if (IsReference(boxed))
                    {
                        Push(value);
                        return;
                    }
                    if (boxed is { Shape: CliTypeShape.GenericInstantiation, ElementType.FullName: "System.Nullable`1" } &&
                        boxed.TypeArguments.Length == 1)
                    {
                        boxed = boxed.TypeArguments[0];
                    }
                    Push(Slot.Of(new FlowSource.Object(new FlowObject.Exact(boxed))));
                    return;
                }
                case CilOperation.NewArray when TypeOf(instruction) is { } element:
                    Pop();
                    Push(Slot.Of(new FlowSource.Object(new FlowObject.Exact(CliTypeIdentity.SzArray(element)))));
                    return;
                case CilOperation.Return:
                    // A runtime-async method returning a task without a value has nothing on the stack at its ret.
                    if (body.Method.Signature.ReturnType != CliValueKind.Void && _stack.Count > 0)
                    {
                        var value = Pop();
                        var returned = body.Method.Signature.ReturnSignatureType;
                        if (body.Method.Definition.IsRuntimeAsync)
                            Flow(value, new FlowNodeKey(FlowNodeKind.AsyncReturn, _caller), null);
                        else if (IsReference(returned))
                            Flow(value, new FlowNodeKey(FlowNodeKind.Return, _caller), returned);
                        else
                            Escape(value);
                    }
                    return;
            }
            Unmodeled(instruction);
        }

        // Pops what the instruction consumes and pushes values the analysis does not follow.
        private void Unmodeled(CilInstruction instruction)
        {
            if ((CilStackEffects.Of(instruction) ?? CompilerEffect(instruction)) is not { } effect)
            {
                _unknownEffect = true;
                return;
            }
            var (pops, pushes) = effect;
            for (var index = 0; index < pops && _stack.Count > 0; index++) Escape(Pop());
            // Only an element load pushes a value of its operand's type; a token or type materialization pushes a
            // handle or a type object, which its operand does not bound.
            var bound = instruction.Operation is CilOperation.LoadArrayElement or CilOperation.LoadRectangularArrayElement &&
                instruction.Operand is CilOperand.TypeIdentity { Value: var type } && IsReference(type) ? type : null;
            for (var index = 0; index < pushes; index++) Push(Slot.Top(bound, $"unmodeled {instruction.Operation}"));
        }

        // What the stack effect table leaves out: the branches and the other instructions that end a block (a leave
        // empties the stack, as the validated height of what follows says), and the compiler's own instructions, as the
        // typed stack validator pops and pushes for them. What they push is a handle, a type or member object, or a
        // number: never followed.
        private static (int Pops, int Pushes)? CompilerEffect(CilInstruction instruction) => instruction.Operation switch
        {
            CilOperation.LoadTypeToken or CilOperation.LoadFieldToken or CilOperation.LoadMethodToken or
                CilOperation.GetTypeFacts or CilOperation.CreateReadOnlySpanFromRva => (0, 1),
            CilOperation.LocalAllocate or CilOperation.MaterializeType or CilOperation.GetObjectType => (1, 1),
            CilOperation.MaterializeMethod or CilOperation.MaterializeField =>
                instruction.Operand is CilOperand.Index { Value: 2 } ? (2, 1) : (1, 1),
            CilOperation.InitializeArrayData => (1, 0),
            CilOperation.CopyBlock or CilOperation.InitializeBlock => (3, 0),
            _ => ControlEffect(instruction.Operation),
        };

        private static (int Pops, int Pushes)? ControlEffect(CilOperation operation) => operation switch
        {
            CilOperation.Branch or CilOperation.Leave or CilOperation.EndFinally or CilOperation.Rethrow => (0, 0),
            CilOperation.Switch or CilOperation.Throw or CilOperation.EndFilter => (1, 0),
            CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or CilOperation.BranchIfGreaterThanSigned or
                CilOperation.BranchIfGreaterThanUnsigned or CilOperation.BranchIfGreaterThanOrEqualSigned or
                CilOperation.BranchIfGreaterThanOrEqualUnsigned or CilOperation.BranchIfLessThanSigned or
                CilOperation.BranchIfLessThanUnsigned or CilOperation.BranchIfLessThanOrEqualSigned or
                CilOperation.BranchIfLessThanOrEqualUnsigned => (2, 0),
            _ => null,
        };

        private void Call(CilInstruction instruction)
        {
            if (owner._calls.Resolve(instruction) is not { } callee)
            {
                Unmodeled(instruction);
                return;
            }
            var constrained = _constrained;
            _constrained = null;
            var count = callee.Signature.ParameterTypes.Length;
            var hasThis = !callee.Definition.IsStatic;
            var arguments = new Slot[count];
            for (var index = count - 1; index >= 0; index--) arguments[index] = Pop();
            var receiver = hasThis ? Pop() : null;
            if (receiver is not null && constrained is not null) receiver = ThroughAddress(receiver, constrained);
            var returned = callee.Signature.ReturnSignatureType;
            var returnsReference = IsReference(returned);
            var key = owner._siteKeys.Build(_caller, instruction.Offset);
            var result = new FlowNodeKey(FlowNodeKind.Result, _caller, instruction.Offset);
            if (owner._isDispatchSite(key) && receiver is not null)
            {
                Site(FlowSiteKind.Dispatch, key, callee, receiver, arguments, returnsReference ? result : null);
                PushResult(returnsReference, result);
                return;
            }
            if (hasThis && receiver is not null && callee.Definition.Name == "Invoke" &&
                owner._delegates.Recognize(callee.DeclaringType))
            {
                Site(FlowSiteKind.DelegateInvoke, key, callee, receiver, arguments, returnsReference ? result : null);
                PushResult(returnsReference, result);
                return;
            }
            // A call whose result the analysis models still runs its callee's body where the program has one
            // (Delegate.Remove calls RemoveImpl on its source): its arguments reach the callee's parameters and the
            // callee is followed, and only the result is the modeled one.
            var modeled = Intrinsic(callee, receiver, arguments, result, returned);
            if (receiver is not null && !callee.DeclaringType.IsValueType)
                Flow(receiver, new FlowNodeKey(FlowNodeKind.Parameter, callee.CanonicalName, 0), callee.DeclaringType);
            else if (receiver is not null)
                Escape(receiver);
            var first = hasThis ? 1 : 0;
            for (var index = 0; index < count; index++)
            {
                var type = callee.Signature.ParameterSignatureTypes[index];
                if (IsReference(type))
                    Flow(arguments[index], new FlowNodeKey(FlowNodeKind.Parameter, callee.CanonicalName, index + first), type);
                else
                    Escape(arguments[index]);
            }
            _reached.Add(callee);
            if (modeled is { } value)
            {
                Push(value);
                return;
            }
            Push(returnsReference
                ? Slot.Of(new FlowSource.Node(new FlowNodeKey(FlowNodeKind.Return, callee.CanonicalName), [])) with
                {
                    DirectCallee = callee,
                }
                : callee.Definition.IsRuntimeAsync ? Slot.Empty with { DirectCallee = callee } : Slot.Empty);
        }

        // Calls whose flow the analysis models instead of their bodies: the object's type does not change through
        // Unsafe.As or MemberwiseClone; Combine's result invokes both operands' targets; an await of a runtime-async
        // call gives that method's returned T.
        private Slot? Intrinsic(MethodInstanceModel callee, Slot? receiver, Slot[] arguments, FlowNodeKey result,
            CliTypeIdentity returned)
        {
            var declaring = callee.DeclaringType.FullName;
            var name = callee.Definition.Name;
            switch (declaring, name, arguments.Length)
            {
                case ("System.Runtime.CompilerServices.Unsafe", "As", 1) when callee.MethodArguments.Length == 1 &&
                    IsReference(callee.Signature.ParameterSignatureTypes[0]):
                    Flow(arguments[0], result, returned);
                    _reinterpretations.Add((callee.MethodArguments[0], result));
                    return Slot.Of(new FlowSource.Node(result, []));
                case ("System.Object", "MemberwiseClone", 0) when receiver is not null:
                    Flow(receiver, result, null);
                    return Slot.Of(new FlowSource.Node(result, []));
                case ("System.Delegate", "Combine", 2):
                case ("System.Delegate", "Remove", 2):
                case ("System.Delegate", "RemoveAll", 2):
                    Flow(arguments[0], result, returned);
                    if (name == "Combine") Flow(arguments[1], result, returned);
                    else Escape(arguments[1]);
                    return Slot.Of(new FlowSource.Node(result, []));
                case ("System.Runtime.CompilerServices.AsyncHelpers", "Await", 1):
                    if (!IsReference(returned))
                    {
                        Escape(arguments[0]);
                        return Slot.Empty;
                    }
                    if (arguments[0].DirectCallee is { Definition.IsRuntimeAsync: true } awaited)
                    {
                        _reached.Add(awaited);
                        return Slot.Of(new FlowSource.Node(new FlowNodeKey(FlowNodeKind.AsyncReturn,
                            awaited.CanonicalName), []));
                    }
                    Escape(arguments[0]);
                    return Slot.Top(returned, "await of a task the analysis does not follow");
                default:
                    return null;
            }
        }

        private void Site(FlowSiteKind kind, string key, MethodInstanceModel callee, Slot receiver, Slot[] arguments,
            FlowNodeKey? result)
        {
            Flow(receiver, new FlowNodeKey(FlowNodeKind.Receiver, key), callee.DeclaringType);
            for (var index = 0; index < arguments.Length; index++)
            {
                var type = callee.Signature.ParameterSignatureTypes[index];
                if (IsReference(type)) Flow(arguments[index], new FlowNodeKey(FlowNodeKind.Argument, key, index), type);
                else Escape(arguments[index]);
            }
            _sites.Add(new FlowSite(kind, key, callee, arguments.Length, result));
        }

        private void NewObject(CilInstruction instruction)
        {
            if (owner._calls.Resolve(instruction) is not { } constructor)
            {
                Unmodeled(instruction);
                return;
            }
            var count = constructor.Signature.ParameterTypes.Length;
            var arguments = new Slot[count];
            for (var index = count - 1; index >= 0; index--) arguments[index] = Pop();
            var type = constructor.DeclaringType;
            if (owner._delegates.Recognize(type) && count == 2 && !arguments[1].Functions.IsDefaultOrEmpty)
            {
                var objects = ImmutableArray.CreateBuilder<FlowSource>(arguments[1].Functions.Length);
                foreach (var target in arguments[1].Functions)
                {
                    objects.Add(new FlowSource.Object(new FlowObject.Delegate(type, target)));
                    // The delegate passes its bound object as the target's this, or as a closed static's first
                    // argument (an open static's is null, which adds nothing).
                    var boundType = !target.Definition.IsStatic ? target.DeclaringType
                        : target.Signature.ParameterSignatureTypes.FirstOrDefault();
                    if (boundType is not null && IsReference(boundType))
                        Flow(arguments[0], new FlowNodeKey(FlowNodeKind.Parameter, target.CanonicalName, 0), boundType);
                    _reached.Add(target);
                }
                Push(new Slot(objects.MoveToImmutable()));
                return;
            }
            for (var index = 0; index < count; index++)
            {
                var parameter = constructor.Signature.ParameterSignatureTypes[index];
                if (IsReference(parameter))
                {
                    Flow(arguments[index], new FlowNodeKey(FlowNodeKind.Parameter, constructor.CanonicalName, index + 1),
                        parameter);
                }
                else
                {
                    Escape(arguments[index]);
                }
            }
            _reached.Add(constructor);
            if (type.IsValueType)
            {
                Push(Slot.Empty);
                return;
            }
            var created = new FlowSource.Object(new FlowObject.Exact(type));
            _edges.Add(new(created, new FlowNodeKey(FlowNodeKind.Parameter, constructor.CanonicalName, 0), type));
            Push(Slot.Of(created));
        }

        private void PushResult(bool returnsReference, FlowNodeKey result) =>
            Push(returnsReference ? Slot.Of(new FlowSource.Node(result, [])) : Slot.Empty);

        // A constrained call's receiver is an address: a reference type's call reads the reference there.
        private static Slot ThroughAddress(Slot receiver, CliTypeIdentity constrained)
        {
            if (!IsReference(constrained)) return Slot.Empty;
            return receiver.Address is { } node
                ? Slot.Of(new FlowSource.Node(node, []))
                : Slot.Top(constrained, "constrained call through an address of unknown origin");
        }

        private Slot Cast(Slot value, CliTypeIdentity type)
        {
            if (!IsReference(type))
            {
                Escape(value);
                return Slot.Empty;
            }
            Escape(value);
            var sources = ImmutableArray.CreateBuilder<FlowSource>(value.Sources.Length);
            foreach (var source in value.Sources)
            {
                sources.Add(source switch
                {
                    FlowSource.Node node => node with { Casts = node.Casts.Add(type) },
                    FlowSource.Top top => new FlowSource.Top(top.Bound.And(type)),
                    // An object's type is exact: the graph filters it with the others when it applies the cast.
                    _ => new FlowSource.Node(CastNode(source), [type]),
                });
            }
            return new Slot(sources.MoveToImmutable());
        }

        // An object constant to cast goes through a node of its own, so the graph applies the type test.
        private FlowNodeKey CastNode(FlowSource source)
        {
            var key = new FlowNodeKey(FlowNodeKind.Result, _caller, -1 - _edges.Count);
            _edges.Add(new(source, key, null));
            return key;
        }

        private Slot LoadField(CilOperand operand)
        {
            if (Field(operand) is not { } field || !IsReference(field.Type)) return Slot.Empty;
            if (field.Overlaps) Seed(field.Key, field.Type, "explicit-layout field");
            else if (field.RuntimeWritten) Seed(field.Key, field.Type, "field the runtime writes");
            return Slot.Of(new FlowSource.Node(field.Key, []));
        }

        private void StoreField(CilOperand operand, Slot value)
        {
            if (Field(operand) is { } field && IsReference(field.Type)) Flow(value, field.Key, field.Type);
            else Escape(value);
        }

        private Slot FieldAddress(CilOperand operand) =>
            Field(operand) is { Overlaps: false } field
                ? Slot.Empty with { Address = field.Key, ReferenceAddress = IsReference(field.Type) }
                : Slot.Empty;

        private (FlowNodeKey Key, CliTypeIdentity Type, bool Overlaps, bool RuntimeWritten)? Field(CilOperand operand)
        {
            switch (operand)
            {
                case CilOperand.FieldInstance { Value: var field }:
                {
                    var name = field.DeclaringType.TypeArguments.IsDefaultOrEmpty
                        ? field.Definition.Key.ToString()
                        : $"{field.Definition.Key}|{field.DeclaringType.CanonicalName}";
                    return (new FlowNodeKey(FlowNodeKind.Field, name), field.FieldType,
                        field.Definition.ExplicitOffset is not null, owner._runtimeFields.IsWritten(field.Definition));
                }
                case CilOperand.Entity { Key: var key }:
                {
                    var field = owner._fields.GetField(key);
                    return (new FlowNodeKey(FlowNodeKind.Field, key.ToString()), field.SignatureType,
                        field.ExplicitOffset is not null, owner._runtimeFields.IsWritten(field));
                }
                default:
                    return null;
            }
        }

        private CliTypeIdentity? TypeOf(CilInstruction instruction) =>
            instruction.Operand is CilOperand.TypeIdentity or CilOperand.ExactType or CilOperand.Entity
                ? owner._typeOperands.Resolve(instruction, body.Method)
                : null;

        private FlowNodeKey Parameter(int argument) => new(FlowNodeKind.Parameter, _caller, argument);

        private FlowNodeKey Local(int local) => new(FlowNodeKind.Local, _caller, local);

        private CliTypeIdentity? ParameterType(int argument)
        {
            if (!body.Method.Definition.IsStatic)
            {
                if (argument == 0) return body.Method.DeclaringType.IsValueType ? null : body.Method.DeclaringType;
                argument--;
            }
            var parameters = body.Method.Signature.ParameterSignatureTypes;
            return (uint)argument < (uint)parameters.Length ? parameters[argument] : null;
        }

        private CliTypeIdentity? LocalType(int local)
        {
            var locals = body.Body.LocalSignatureTypes;
            return (uint)local < (uint)locals.Length ? locals[local] : null;
        }

        private static Slot Load(FlowNodeKey key, CliTypeIdentity? type) =>
            type is not null && IsReference(type) ? Slot.Of(new FlowSource.Node(key, [])) : Slot.Empty;

        private static Slot AddressOf(FlowNodeKey key, CliTypeIdentity? type) =>
            Slot.Empty with { Address = key, ReferenceAddress = type is not null && IsReference(type) };

        private void Flow(Slot value, FlowNodeKey target, CliTypeIdentity? type)
        {
            Escape(value);
            foreach (var source in value.Sources) _edges.Add(new(source, target, type));
        }

        // An address or function pointer used other than as the analysis follows: what it points at may change
        // where the analysis does not see, and the methods it names may be called with anything.
        private void Escape(Slot value)
        {
            if (value.Address is { } node && value.ReferenceAddress) Seed(node, null, "address escapes");
            if (!value.Functions.IsDefaultOrEmpty) _addressEscapes.AddRange(value.Functions);
        }

        private void Seed(FlowNodeKey key, CliTypeIdentity? bound, string cause) =>
            _edges.Add(new(new FlowSource.Top(FlowBound.Of(bound, cause)), key, bound));

        private void Push(Slot slot) => _stack.Add(slot);

        private Slot Pop()
        {
            if (_stack.Count == 0) return Slot.Top(null, "stack underflow");
            var top = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            return top;
        }
    }

    private static bool IsReference(CliTypeIdentity type) => type.StackKind == CliValueKind.ManagedReference;
}
