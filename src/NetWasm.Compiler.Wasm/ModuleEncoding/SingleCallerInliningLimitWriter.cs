using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.ModuleEncoding;

/// <summary>
/// Decorator: keeps Binaryen's single-caller inlining within its own combined-size limit before the hints are
/// written. Binaryen inlines every function that has one caller, and it checks its limit on the size of a function
/// after inlining for each inlining against the caller's size before the round. A caller of thousands of
/// single-caller functions, such as a dispatch over every override of a virtual method, therefore absorbs them all
/// in one round: DocumentFormat.OpenXml's metadata dispatch grew to 12 MB, over the engines' 7,654,321-byte limit
/// for a function. Where a function and the single-caller functions it would absorb, transitively, exceed the limit,
/// the largest of them are marked never-inline until the rest fit; they stay calls.
/// </summary>
internal sealed class SingleCallerInliningLimitWriter(
    IFunctionInlineAnnotationWriter inner,
    IWasmIndexOperandScanner scanner) : IFunctionInlineAnnotationWriter
{
    /// <summary>Binaryen's --inline-max-combined-binary-size default.</summary>
    internal const long CombinedSizeLimit = 400 * 1024;

    private const byte RefFunc = 0xD2;

    private readonly IFunctionInlineAnnotationWriter _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IWasmIndexOperandScanner _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));

    public void Write(IWasmBinaryWriter output, IReadOnlyList<WasmFunctionDefinition> functions, int functionImportCount)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(functionImportCount);
        // A module whose whole code fits cannot grow a function past the limit.
        if (functions.Sum(function => (long)function.Body.Length) <= CombinedSizeLimit)
        {
            _inner.Write(output, functions, functionImportCount);
            return;
        }
        var limited = Limit(functions, functionImportCount);
        _inner.Write(output, limited.Count == 0
            ? functions
            : [.. functions.Select((function, index) => limited.Contains(index) ? function with { IsNoInlining = true } : function)],
            functionImportCount);
    }

    // The defined functions to mark never-inline, by their index among the definitions.
    internal HashSet<int> Limit(IReadOnlyList<WasmFunctionDefinition> functions, int functionImportCount)
    {
        var count = functions.Count;
        var callees = new List<int>?[count];
        var calls = new int[count];
        var referenced = new bool[count];
        for (var caller = 0; caller < count; caller++)
        {
            var body = functions[caller].Body;
            foreach (var operand in _scanner.Scan(body))
            {
                var callee = (long)operand.Index - functionImportCount;
                if (operand.Space != WasmIndexSpace.Function || callee < 0 || callee >= count)
                    continue;
                // A ref.func makes the function a value; Binaryen inlines no such function by its one caller.
                if (body[operand.Offset - 1] == RefFunc)
                {
                    referenced[callee] = true;
                    continue;
                }
                calls[callee]++;
                if (callee != caller)
                    (callees[caller] ??= []).Add((int)callee);
            }
        }

        bool SingleCaller(int function) => calls[function] == 1 && !referenced[function] &&
            !functions[function].IsNoInlining && !functions[function].IsAggressiveInlining;

        // What each function weighs once its single-caller callees are inlined into it, computed callees first.
        var absorbed = new long[count];
        var state = new byte[count]; // 0 unvisited, 1 in progress, 2 done
        var limited = new HashSet<int>();
        var stack = new Stack<(int Function, bool Expanded)>();
        for (var root = 0; root < count; root++)
        {
            if (state[root] != 0) continue;
            stack.Push((root, false));
            while (stack.Count != 0)
            {
                var (function, expanded) = stack.Pop();
                if (!expanded)
                {
                    if (state[function] != 0) continue;
                    state[function] = 1;
                    stack.Push((function, true));
                    foreach (var callee in callees[function] ?? [])
                    {
                        if (state[callee] == 0 && SingleCaller(callee)) stack.Push((callee, false));
                    }
                    continue;
                }
                var inlined = (callees[function] ?? [])
                    .Where(callee => SingleCaller(callee) && state[callee] == 2)
                    .Select(callee => (Callee: callee, Size: absorbed[callee]))
                    .ToList();
                var total = functions[function].Body.Length + inlined.Sum(entry => entry.Size);
                if (total > CombinedSizeLimit)
                {
                    foreach (var (callee, size) in inlined.OrderByDescending(entry => entry.Size).ThenBy(entry => entry.Callee))
                    {
                        if (total <= CombinedSizeLimit) break;
                        limited.Add(callee);
                        total -= size;
                    }
                }
                absorbed[function] = total;
                state[function] = 2;
            }
        }
        return limited;
    }
}
