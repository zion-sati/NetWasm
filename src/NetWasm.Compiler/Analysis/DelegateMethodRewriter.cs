using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// new D(M).Method, with M named by ldftn, is M's MethodInfo: the method materializes from its token, as
// ldtoken M + MethodBase.GetMethodFromHandle does for an expression lambda, and the delegate goes. The receiver
// (ldnull for a static method) must push one value without side effects, and the four instructions must sit
// in one block, so no branch lands between them. Only M gets a descriptor; Delegate.Method on a delegate
// whose target the compiler cannot see here keeps throwing, so the profile stays pay-for-play.
internal sealed class DelegateMethodRewriter(
    IControlFlowGraphBuilder graphs,
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly ISymbolFormatter _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.Instructions.Any(static instruction => instruction.Operation == CilOperation.LoadFunction))
        {
            return body;
        }

        var rewritten = body.Instructions.ToDictionary(instruction => instruction.Offset);
        var changed = false;
        foreach (var block in _graphs.Build(body).Blocks)
        {
            var operations = block.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
            for (var index = 3; index < operations.Length; index++)
            {
                if (operations[index] is not { Operation: CilOperation.Call or CilOperation.CallVirtual } read ||
                    operations[index - 2] is not { Operation: CilOperation.LoadFunction, Operand: CilOperand.MethodInstance target } function ||
                    operations[index - 1] is not { Operation: CilOperation.NewObject } creation ||
                    !IsPureValue(operations[index - 3]) ||
                    !ReadsDelegateMethod(read) ||
                    !CreatesDelegate(creation))
                {
                    continue;
                }

                var receiver = operations[index - 3];
                rewritten[receiver.Offset] = receiver with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
                rewritten[function.Offset] = function with { Operation = CilOperation.LoadMethodToken };
                // The declaring type's handle selects M's instantiation, as GetMethodFromHandle(method, type)
                // does for a method of a generic type.
                rewritten[creation.Offset] = creation with
                {
                    Operation = CilOperation.LoadTypeToken,
                    Operand = new CilOperand.TypeIdentity(target.Value.DeclaringType),
                };
                rewritten[read.Offset] = read with { Operation = CilOperation.MaterializeMethod, Operand = new CilOperand.Index(2) };
                changed = true;
            }
        }
        return changed ? body with { Instructions = [.. body.Instructions.Select(instruction => rewritten[instruction.Offset])] } : body;
    }

    // Pushes one value without side effects, so it can go with the delegate.
    private static bool IsPureValue(CilInstruction instruction) => instruction.Operation is
        CilOperation.LoadNull or CilOperation.LoadLocal or CilOperation.LoadArgument;

    // Delegate.Method, or RuntimeReflectionExtensions.GetMethodInfo(Delegate).
    private bool ReadsDelegateMethod(CilInstruction read) =>
        _calledMethods.Resolve(read) is { } method &&
        (_symbols.Format(method.Definition.DeclaringType), method.Definition.Name) is
            ("System.Delegate", "get_Method") or
            ("System.Reflection.RuntimeReflectionExtensions", "GetMethodInfo");

    // A delegate constructor takes the target object and the function pointer. Delegate.Method reads the
    // object it creates, which verification only allows for a delegate type.
    private bool CreatesDelegate(CilInstruction creation) =>
        _calledMethods.Resolve(creation) is { Definition.Name: ".ctor" } constructor &&
        constructor.Signature.ParameterSignatureTypes.Select(static type => type.StackKind).SequenceEqual(DelegateConstructor);

    private static readonly CliValueKind[] DelegateConstructor = [CliValueKind.ManagedReference, CliValueKind.NativeInt];
}
