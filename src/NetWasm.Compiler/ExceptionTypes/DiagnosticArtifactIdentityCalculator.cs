using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Compiler.ExceptionTypes;

public interface IDiagnosticArtifactIdentityCalculator
{
    string CalculateBuildId(IEnumerable<string> semanticInputs);
}

public sealed class DiagnosticArtifactIdentityCalculator :
    IDiagnosticArtifactIdentityCalculator
{
    public string CalculateBuildId(IEnumerable<string> semanticInputs)
    {
        ArgumentNullException.ThrowIfNull(semanticInputs);
        var canonical = string.Join("\n", semanticInputs.Order(StringComparer.Ordinal));
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
