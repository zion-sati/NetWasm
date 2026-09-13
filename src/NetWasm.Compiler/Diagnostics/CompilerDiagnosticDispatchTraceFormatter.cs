using System;
using System.Linq;
using System.Text;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticDispatchTraceFormatter :
    ICompilerDiagnosticDispatchTraceFormatter
{
    public string FormatDispatch(ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var text = new StringBuilder();
        text.AppendLine("[dispatch]");
        foreach (var site in program.DispatchCallSites.OrderBy(
                     pair => pair.Key,
                     StringComparer.Ordinal))
        {
            text.Append(site.Key).Append(" declaration=")
                .Append(site.Value.Declaration.CanonicalName).AppendLine();
            foreach (var target in site.Value.Targets.OrderBy(
                         target => target.ReceiverType.CanonicalName,
                         StringComparer.Ordinal))
            {
                text.Append("  ").Append(target.ReceiverType.CanonicalName)
                    .Append(" -> ").Append(target.Method.CanonicalName).AppendLine();
            }
        }
        text.AppendLine();
        return text.ToString().ReplaceLineEndings("\n");
    }
}
