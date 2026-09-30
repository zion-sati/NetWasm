// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class LocalVariables
    {
        private readonly Dictionary<ParameterExpression, Stack<int>> _bindings =
            new();
        private int _currentLocalCount;

        internal int LocalCount { get; private set; }

        internal LocalDefinition Define(ParameterExpression variable)
        {
            var index = _currentLocalCount++;
            if (_currentLocalCount > LocalCount)
            {
                LocalCount = _currentLocalCount;
            }

            if (!_bindings.TryGetValue(variable, out var scopes))
            {
                scopes = new Stack<int>();
                _bindings.Add(variable, scopes);
            }
            scopes.Push(index);
            return new LocalDefinition(variable, index);
        }

        internal void Undefine(LocalDefinition definition)
        {
            if (!_bindings.TryGetValue(definition.Variable, out var scopes) ||
                scopes.Count == 0 || scopes.Peek() != definition.Index)
            {
                throw new InvalidOperationException(
                    "The interpreted local scope is inconsistent.");
            }

            scopes.Pop();
            if (scopes.Count == 0)
            {
                _bindings.Remove(definition.Variable);
            }
            _currentLocalCount--;
        }

        internal int Resolve(ParameterExpression variable)
        {
            if (_bindings.TryGetValue(variable, out var scopes) &&
                scopes.Count != 0)
            {
                return scopes.Peek();
            }
            throw new InvalidOperationException(
                "A parameter expression is referenced outside its defining scope.");
        }
    }

    internal readonly struct LocalDefinition
    {
        internal LocalDefinition(ParameterExpression variable, int index)
        {
            Variable = variable;
            Index = index;
        }

        internal ParameterExpression Variable { get; }

        internal int Index { get; }
    }
}
