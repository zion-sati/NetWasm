using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawWitImportLayoutBuilder
{
    RawWitImportLayout Build(RawWitImportLayoutRequest request);
}

public sealed record RawWitImportLayoutRegistration(Type DeclarationType, IRawWitImportLayoutBuilder Builder);

public sealed class RawWitImportLayoutBuilder : IRawWitImportLayoutBuilder
{
    private readonly ImmutableDictionary<Type, IRawWitImportLayoutBuilder> _builders;

    public RawWitImportLayoutBuilder(IEnumerable<RawWitImportLayoutRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var builders = ImmutableDictionary.CreateBuilder<Type, IRawWitImportLayoutBuilder>();
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            ArgumentNullException.ThrowIfNull(registration.DeclarationType);
            ArgumentNullException.ThrowIfNull(registration.Builder);
            if (!typeof(RawWitImportDeclaration).IsAssignableFrom(registration.DeclarationType) || registration.DeclarationType.IsAbstract)
            {
                throw new ArgumentException("raw layout registration must name a concrete WIT declaration type", nameof(registrations));
            }
            if (!builders.TryAdd(registration.DeclarationType, registration.Builder))
            {
                throw new ArgumentException("raw layout declaration type is registered more than once", nameof(registrations));
            }
        }
        if (!builders.ContainsKey(typeof(RawWitImportDeclaration.Callable)) || !builders.ContainsKey(typeof(RawWitImportDeclaration.Resource)))
        {
            throw new ArgumentException("raw layout registrations must include callable and resource builders", nameof(registrations));
        }
        _builders = builders.ToImmutable();
    }

    public RawWitImportLayout Build(RawWitImportLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Declaration);
        return _builders.TryGetValue(request.Declaration.GetType(), out var builder)
            ? builder.Build(request)
            : throw ComponentException.Invalid("raw WIT declaration has no registered layout builder");
    }
}
