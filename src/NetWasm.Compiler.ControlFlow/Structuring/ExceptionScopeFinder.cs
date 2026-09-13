using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ExceptionScopeFinder : IExceptionScopeFinder
{
    public ExceptionScope? Find(
        ExceptionGroupSource container,
        ExceptionGroupSource candidate)
    {
        if (candidate.TryOffset >= container.TryOffset &&
            candidate.Extent <= container.TryEnd)
        {
            return new ExceptionScope(
                ExceptionScopeKind.Protected,
                -1,
                container.TryOffset,
                container.TryEnd);
        }
        for (var clauseIndex = 0;
             clauseIndex < container.Regions.Length;
             clauseIndex++)
        {
            var region = container.Regions[clauseIndex];
            if (region.FilterOffset is int filterOffset &&
                candidate.TryOffset >= filterOffset &&
                candidate.Extent <= region.HandlerOffset)
            {
                return new ExceptionScope(
                    ExceptionScopeKind.Filter,
                    clauseIndex,
                    filterOffset,
                    region.HandlerOffset);
            }
            var handlerEnd = checked(region.HandlerOffset + region.HandlerLength);
            if (candidate.TryOffset >= region.HandlerOffset &&
                candidate.Extent <= handlerEnd)
            {
                return new ExceptionScope(
                    ExceptionScopeKind.Handler,
                    clauseIndex,
                    region.HandlerOffset,
                    handlerEnd);
            }
        }
        return null;
    }
}
