using System;
using System.Linq;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed class AttributeQueryPlanner(IAttributeMatchSelector matches,
    IAttributeConstructionPlanner constructions) : IAttributeQueryPlanner
{
    public AttributeQueryEntry Plan(ResolvedAttributeQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var existence = query.Call.Operation == AttributeQueryOperation.IsDefined;
        return new(query.Target, query.Filter, Result(false), Result(true));

        AttributeQueryResult Result(bool inherit)
        {
            var selected = query.InheritConstant is { } constant && constant != inherit ? [] :
                matches.Select(query.Target, query.Filter, inherit, existence);
            return existence ? new AttributeExistenceResult(!selected.IsEmpty) :
                new AttributeRetrievalResult([.. selected.Select(constructions.Plan)]);
        }
    }
}
