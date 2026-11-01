using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// CIL with named labels, numbered by position, for passes' CIL-level tests.
internal sealed class CilBuilder
{
    private readonly List<CilInstruction> _code = [];
    private readonly Dictionary<string, int> _labels = [];
    private readonly List<(int Index, string Label)> _branches = [];
    private int _loops;

    public CilBuilder Op(CilOperation operation, CilOperand? operand = null)
    {
        _code.Add(new(_code.Count, _code.Count + 1, operation, operand ?? new CilOperand.None()));
        return this;
    }

    // A constant of the load's kind.
    public CilBuilder Op(CilOperation operation, double value) => Op(operation, operation switch
    {
        CilOperation.LoadInt32 => new CilOperand.ConstantI4((int)value),
        CilOperation.LoadInt64 => new CilOperand.ConstantI8((long)value),
        CilOperation.LoadFloat32 => new CilOperand.ConstantF4((float)value),
        _ => new CilOperand.ConstantF8(value),
    });

    public CilBuilder Local(CilOperation operation, int index) => Op(operation, new CilOperand.Index(index));

    public CilBuilder Label(string name)
    {
        _labels.Add(name, _code.Count);
        return this;
    }

    public CilBuilder Branch(CilOperation operation, string label)
    {
        _branches.Add((_code.Count, label));
        return Op(operation);
    }

    // for (counter = 0; counter < 10; counter++) body; the shape C# compiles a counted loop to.
    public CilBuilder Loop(int counter, Func<CilBuilder, CilBuilder> body)
    {
        var loop = $"loop{_loops++}";
        Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, counter).Branch(CilOperation.Branch, $"{loop}.test")
            .Label($"{loop}.start");
        body(this);
        return Local(CilOperation.LoadLocal, counter).Op(CilOperation.LoadInt32, 1).Op(CilOperation.Add)
            .Local(CilOperation.StoreLocal, counter)
            .Label($"{loop}.test").Local(CilOperation.LoadLocal, counter).Op(CilOperation.LoadInt32, 10)
            .Branch(CilOperation.BranchIfLessThanSigned, $"{loop}.start");
    }

    public CilBuilder Return() => Op(CilOperation.Return);

    public ImmutableArray<CilInstruction> Build()
    {
        foreach (var (index, label) in _branches)
        {
            _code[index] = _code[index] with { Operand = new CilOperand.BranchTarget(_labels[label]) };
        }
        return [.. _code];
    }
}
