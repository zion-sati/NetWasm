using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Analysis.Loops.Elementwise.ElementwiseCil;
using static NetWasm.Compiler.Analysis.Loops.Elementwise.ElementwiseNodes;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

// The vector code a prefix emits: its values the loop does not change,
// broadcast once, its folds' vectors, and each node's lanes.
internal sealed class ElementwiseVectorCode
{
    private readonly IVectorLoopMethodResolver _methods;
    private readonly ImmutableArray<CliValueKind>.Builder _locals;
    private readonly ImmutableArray<CliTypeIdentity>.Builder _localTypes;
    private readonly int? _counter;
    private readonly CliValueKind _kind;
    private readonly Dictionary<string, int> _broadcasts = [];
    private int? _iota;

    internal ElementwiseVectorCode(
        IVectorLoopMethodResolver methods,
        CliValueKind kind,
        ImmutableArray<CliValueKind>.Builder locals,
        ImmutableArray<CliTypeIdentity>.Builder localTypes,
        int? counter)
    {
        _methods = methods;
        _kind = kind;
        _locals = locals;
        _localTypes = localTypes;
        _counter = counter;
        Lane = LaneType(kind);
        Lanes = 16 / Size(kind);
        VectorType = methods.Resolve(VectorLoopMethod.Broadcast, Lane).Signature.ReturnSignatureType;
    }

    internal List<CilInstruction> Code { get; } = [];

    internal CliTypeIdentity Lane { get; }

    internal int Lanes { get; }

    internal CliTypeIdentity VectorType { get; }

    internal void Add(CilOperation operation, CilOperand? operand = null) =>
        Code.Add(Instruction(operation, operand ?? new CilOperand.None()));

    internal void Emit(IEnumerable<CilInstruction> code)
    {
        foreach (var instruction in code) Code.Add(Copy(instruction));
    }

    internal void Constant(int value) => Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(value));

    internal void Call(MethodInstanceModel method) => Add(CilOperation.Call, new CilOperand.MethodInstance(method));

    // The counter plus offset.
    internal void Counter(int offset)
    {
        Add(CilOperation.LoadLocal, new CilOperand.Index(_counter!.Value));
        if (offset == 0) return;
        Constant(offset);
        Add(CilOperation.Add);
    }

    internal int NewLocal(CliTypeIdentity type)
    {
        _locals.Add(type.StackKind);
        _localTypes.Add(type);
        return _locals.Count - 1;
    }

    internal int Broadcast(ImmutableArray<CilInstruction> code)
    {
        var key = string.Join(';', code.Select(static instruction => $"{instruction.Operation}:{instruction.Operand}"));
        if (_broadcasts.TryGetValue(key, out var existing)) return existing;
        Emit(code);
        Call(_methods.Resolve(VectorLoopMethod.Broadcast, Lane));
        var local = NewLocal(VectorType);
        Add(CilOperation.StoreLocal, new CilOperand.Index(local));
        return _broadcasts[key] = local;
    }

    // Broadcasts a node's values the loop does not change; the lanes' own
    // counter offsets 0, 1, 2 and 3 for a value that reads the counter.
    internal void Hoist(ElementwiseNode node)
    {
        switch (node)
        {
            case Invariant invariant:
                Broadcast(invariant.Code);
                break;
            case Linear { Scale: 0, Offset: var value }:
                Broadcast([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(value))]);
                break;
            case Linear linear:
                if (_iota is null)
                {
                    Constant(0);
                    Constant(1);
                    Constant(2);
                    Constant(3);
                    Call(_methods.Resolve(VectorLoopMethod.CreateInt32, Lane));
                    _iota = NewLocal(VectorType);
                    Add(CilOperation.StoreLocal, new CilOperand.Index(_iota.Value));
                }
                if (linear.Scale != 1) Broadcast([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(linear.Scale))]);
                if (linear.Offset != 0) Broadcast([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(linear.Offset))]);
                break;
            case Unary unary:
                Hoist(unary.Operand);
                break;
            case Binary binary:
                Hoist(binary.Left);
                Hoist(binary.Right);
                break;
            case Shift shift:
                Hoist(shift.Value);
                break;
        }
    }

    // Pushes a node's lanes.
    internal void Value(ElementwiseNode node)
    {
        switch (node)
        {
            case Element { ByReference: true } element:
                Code.Add(Copy(element.Array));
                Constant(element.Offset);
                Add(CilOperation.ConvertNativeUInt);
                Call(_methods.Resolve(VectorLoopMethod.Load, Lane));
                break;
            // Extended to a native int as the scalar index was: Unsafe.Add of an
            // int sign-extends it.
            case Element { Span: { } indexer } element:
                Code.Add(Copy(element.Array));
                Counter(element.Offset);
                Code.Add(indexer.Conversion is { } conversion
                    ? Copy(conversion)
                    : Instruction(CilOperation.ConvertNativeInt, new CilOperand.None()));
                Call(_methods.Resolve(VectorLoopMethod.Load, Lane));
                break;
            case Element element:
                Code.Add(Copy(element.Array));
                Call(_methods.Resolve(VectorLoopMethod.ArrayData, element.Type));
                Counter(element.Offset);
                Add(CilOperation.ConvertNativeUInt);
                Call(_methods.Resolve(VectorLoopMethod.Load, Lane));
                break;
            case Invariant invariant:
                Add(CilOperation.LoadLocal, new CilOperand.Index(Broadcast(invariant.Code)));
                break;
            case Linear { Scale: 0, Offset: var value }:
                Add(CilOperation.LoadLocal, new CilOperand.Index(
                    Broadcast([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(value))])));
                break;
            case Linear linear:
                // scale * (i + lane) + offset.
                Counter(0);
                Call(_methods.Resolve(VectorLoopMethod.Broadcast, Lane));
                Add(CilOperation.LoadLocal, new CilOperand.Index(_iota!.Value));
                Call(_methods.Resolve(new VectorLoopOperator("op_Addition"), Lane));
                if (linear.Scale != 1)
                {
                    Add(CilOperation.LoadLocal, new CilOperand.Index(
                        Broadcast([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(linear.Scale))])));
                    Call(_methods.Resolve(new VectorLoopOperator("op_Multiply"), Lane));
                }
                if (linear.Offset != 0)
                {
                    Add(CilOperation.LoadLocal, new CilOperand.Index(
                        Broadcast([Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(linear.Offset))])));
                    Call(_methods.Resolve(new VectorLoopOperator("op_Addition"), Lane));
                }
                break;
            case Unary unary:
                Value(unary.Operand);
                Call(_methods.Resolve(new VectorLoopOperator(unary.Operation == CilOperation.Negate ? "op_UnaryNegation" : "op_OnesComplement"), Lane));
                break;
            case Binary binary:
                Value(binary.Left);
                Value(binary.Right);
                Call(_methods.Resolve(new VectorLoopOperator(Operator(binary.Operation)), Lane));
                break;
            default:
                // The remaining node shifts a value.
                var shift = (Shift)node;
                Value(shift.Value);
                Constant(shift.Count);
                Call(_methods.Resolve(new VectorLoopOperator(shift.Operation switch
                {
                    CilOperation.ShiftLeft => "op_LeftShift",
                    CilOperation.ShiftRightSigned => "op_RightShift",
                    _ => "op_UnsignedRightShift",
                }), Lane));
                break;
        }
    }

    // Each fold's vector, at its operation's identity: no bits for an
    // addition, an or and an exclusive or, every bit for an and.
    internal List<(Fold Fold, int Vector)> Identities(List<Fold> folds)
    {
        var folded = new List<(Fold Fold, int Vector)>();
        foreach (var fold in folds)
        {
            Constant(fold.Operation == CilOperation.BitwiseAnd ? -1 : 0);
            if (_kind == CliValueKind.I8) Add(CilOperation.ConvertInt64);
            Call(_methods.Resolve(VectorLoopMethod.Broadcast, Lane));
            var vector = NewLocal(VectorType);
            Add(CilOperation.StoreLocal, new CilOperand.Index(vector));
            folded.Add((fold, vector));
        }
        return folded;
    }

    // One vector iteration of each fold.
    internal void Fold(List<(Fold Fold, int Vector)> folded)
    {
        foreach (var (fold, vector) in folded)
        {
            Add(CilOperation.LoadLocal, new CilOperand.Index(vector));
            Value(fold.Value);
            Call(_methods.Resolve(new VectorLoopOperator(Operator(fold.Operation)), Lane));
            Add(CilOperation.StoreLocal, new CilOperand.Index(vector));
        }
    }

    // Each vector's lanes folded into its local.
    internal void Reduce(List<(Fold Fold, int Vector)> folded)
    {
        foreach (var (fold, vector) in folded)
        {
            Add(CilOperation.LoadLocal, new CilOperand.Index(fold.Local));
            if (fold.Operation == CilOperation.Add)
            {
                Add(CilOperation.LoadLocal, new CilOperand.Index(vector));
                Call(_methods.Resolve(VectorLoopMethod.Sum, Lane));
            }
            else
            {
                for (var index = 0; index < Lanes; index++)
                {
                    Add(CilOperation.LoadLocal, new CilOperand.Index(vector));
                    Constant(index);
                    Call(_methods.Resolve(VectorLoopMethod.GetElement, Lane));
                    if (index > 0) Add(fold.Operation);
                }
            }
            Add(fold.Operation);
            Add(CilOperation.StoreLocal, new CilOperand.Index(fold.Local));
        }
    }
}
