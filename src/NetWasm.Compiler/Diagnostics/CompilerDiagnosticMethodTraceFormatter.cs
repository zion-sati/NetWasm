using System;
using System.Globalization;
using System.Linq;
using System.Text;
using StructuredMethod = global::NetWasm.Compiler.ControlFlow.Structured.StructuredMethod;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticMethodTraceFormatter(
    ICompilerCilOperandFormatter operandFormatter,
    ICompilerStructuredMethodFormatter structureFormatter) :
    ICompilerDiagnosticMethodTraceFormatter
{
    private readonly ICompilerCilOperandFormatter _operandFormatter =
        operandFormatter ?? throw new ArgumentNullException(nameof(operandFormatter));
    private readonly ICompilerStructuredMethodFormatter _structureFormatter =
        structureFormatter ?? throw new ArgumentNullException(nameof(structureFormatter));

    public string FormatMethods(
        ISymbolFormatter symbols,
        ReachableProgram program,
        WasmMethodLoweringResult lowering)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(lowering);
        var text = new StringBuilder();
        foreach (var pair in program.Methods
                     .OrderBy(pair => pair.Key.Assembly.Name, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key.MetadataToken))
        {
            WriteMethod(
                text,
                symbols.Format(pair.Value.Method.Definition),
                lowering.Methods[pair.Key],
                program.RootMaps[pair.Key],
                program.AllocatingMethods.Contains(pair.Key));
        }
        foreach (var pair in program.ConstructedMethods.OrderBy(
                     pair => pair.Key,
                     StringComparer.Ordinal))
        {
            WriteMethod(
                text,
                pair.Key,
                lowering.ConstructedMethods[pair.Key],
                program.ConstructedRootMaps[pair.Key],
                program.ConstructedAllocatingMethods.Contains(pair.Key));
        }
        return text.ToString().ReplaceLineEndings("\n");
    }

    private void WriteMethod(
        StringBuilder text,
        string identity,
        StructuredMethod method,
        MethodRootMap roots,
        bool allocating)
    {
        var body = method.Header;
        text.Append("[method ").Append(identity).AppendLine("]");
        text.Append("allocating=").AppendLine(allocating ? "true" : "false");
        text.Append("locals=").AppendJoin(',', body.LocalSignatureTypes.Select(
            type => type.CanonicalName)).AppendLine();
        foreach (var instruction in body.Instructions)
        {
            text.Append("IL_").Append(Hex(instruction.Offset))
                .Append(' ').Append(instruction.Operation);
            if (instruction.Operand is not CilOperand.None)
            {
                text.Append(' ').Append(_operandFormatter.FormatOperand(instruction.Operand));
            }
            if (roots.Safepoints.TryGetValue(instruction.Offset, out var safepoint))
            {
                text.Append(" roots=[").AppendJoin(',', safepoint.Roots).Append(']');
            }
            text.AppendLine();
        }
        text.AppendLine("structure:");
        text.Append(_structureFormatter.FormatStructure(method));
        foreach (var group in method.ExceptionGroups.Values.OrderBy(group => group.Id.Value))
        {
            text.Append("  eh try=IL_").Append(Hex(group.TryOffset))
                .Append(" length=").Append(group.TryLength)
                .Append(" clauses=").AppendJoin(',', group.Clauses.Select(
                    clause => clause.Kind.ToString()))
                .Append(" continuations=").AppendJoin(',', group.NormalContinuations.Select(
                    continuation => $"IL_{Hex(continuation.TargetOffset)}"))
                .AppendLine();
        }
        text.AppendLine();
    }

    private static string Hex(int value) =>
        value.ToString("x4", CultureInfo.InvariantCulture);
}
