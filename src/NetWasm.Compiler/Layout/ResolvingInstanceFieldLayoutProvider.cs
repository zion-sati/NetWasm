using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class ResolvingInstanceFieldLayoutProvider(
    IInstanceFieldLayoutProvider inner,
    ITypeRepository types,
    IFieldRepository fields,
    IValueLayoutResolver resolver) : IInstanceFieldLayoutProvider
{
    public FieldLayout GetFieldLayout(EntityKey field)
    {
        try { return inner.GetFieldLayout(field); }
        catch (CompilerException missing)
            when (missing.Diagnostic.Code == DiagnosticCode.RuntimeContract)
        {
            var model = fields.GetField(field);
            var definition = types.GetTypeDefinition(model.DeclaringType);
            if (model.IsStatic || !definition.IsValueType)
                throw;
            try
            {
                resolver.Resolve(CliTypeIdentity.FromDefinition(definition),
                    definition);
                return inner.GetFieldLayout(field);
            }
            catch (Exception recovery)
                when (recovery is CompilerException or KeyNotFoundException)
            {
                ExceptionDispatchInfo.Capture(missing).Throw();
                throw;
            }
        }
    }

    public FieldLayout GetFieldLayout(FieldInstanceModel field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!field.IsConstructed)
            return GetFieldLayout(field.Definition.Key);
        try { return inner.GetFieldLayout(field); }
        catch (CompilerException missing)
            when (missing.Diagnostic.Code == DiagnosticCode.RuntimeContract)
        {
            if (field.Definition.IsStatic || !field.DeclaringType.IsValueType)
                throw;
            try
            {
                resolver.Resolve(field.DeclaringType);
                return inner.GetFieldLayout(field);
            }
            catch (Exception recovery)
                when (recovery is CompilerException or KeyNotFoundException)
            {
                ExceptionDispatchInfo.Capture(missing).Throw();
                throw;
            }
        }
    }
}
