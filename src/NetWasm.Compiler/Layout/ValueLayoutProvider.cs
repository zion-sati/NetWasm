using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class ValueLayoutProvider(
    IValueLayoutResolver resolver) : IValueLayoutProvider
{
    private readonly IValueLayoutResolver _resolver = resolver ??
        throw new ArgumentNullException(nameof(resolver));

    public ValueLayout GetValueLayout(CliTypeIdentity type) => _resolver.Resolve(type);
}
