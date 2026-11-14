// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: the subset the ported expression types use.
using System.Linq.Expressions;

namespace System.Dynamic.Utils
{
    internal static class ExpressionVisitorUtils
    {
        public static Expression[]? VisitArguments(ExpressionVisitor visitor, IArgumentProvider nodes)
        {
            Expression[]? newNodes = null;
            for (int i = 0, n = nodes.ArgumentCount; i < n; i++)
            {
                Expression curNode = nodes.GetArgument(i);
                Expression node = visitor.Visit(curNode);

                if (newNodes != null)
                {
                    newNodes[i] = node;
                }
                else if (!object.ReferenceEquals(node, curNode))
                {
                    newNodes = new Expression[n];
                    for (int j = 0; j < i; j++)
                    {
                        newNodes[j] = nodes.GetArgument(j);
                    }
                    newNodes[i] = node;
                }
            }
            return newNodes;
        }
    }
}
