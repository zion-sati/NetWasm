using System;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.ComponentModel;

internal sealed class SystemProcessEnvironmentTarget : IProcessEnvironmentTarget
{
    private readonly IDictionary<string, string?> _environment;

    public SystemProcessEnvironmentTarget(IDictionary<string, string?> environment)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public void RemoveByPrefix(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var matches = _environment.Keys
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var name in matches)
        {
            _environment.Remove(name);
        }
    }
}
