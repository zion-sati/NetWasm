using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Gives CoreLib's AppContext.ConfigurationProperties the project's RuntimeHostConfigurationOption items, the host
// properties .NET reads from runtimeconfig.json: one string of each name and value followed by '\0'.
internal sealed class RuntimeConfigurationRewriter(
    ISymbolFormatter symbols,
    ImmutableArray<(string Name, string Value)> properties) : IMethodRewriteRule
{
    private readonly string _properties = string.Concat(properties.Select(property => $"{property.Name}\0{property.Value}\0"));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Method.Name != "ConfigurationProperties" || !body.Method.IsStatic ||
            symbols.Format(body.Method.DeclaringType) != "System.AppContext")
        {
            return body;
        }
        return body with
        {
            MaxStack = 1,
            Locals = [],
            LocalSignatureTypes = [],
            ExceptionRegions = [],
            Instructions =
            [
                new(0, 1, CilOperation.LoadString, new CilOperand.UserString(_properties)),
                new(1, 2, CilOperation.Return, new CilOperand.None()),
            ],
        };
    }
}
